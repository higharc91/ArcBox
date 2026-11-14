namespace Meridian.Logic;

// Combat and monsters (spec §2.7; M3 plan, Task 2). Only in the endgame.
internal sealed partial class Resolver
{
    private static IEnumerable<Token> Monsters(GameState state) => state.Board?.Tokens.Where(t => t.Stats is not null && t.At is not null) ?? [];

    private static void LegalCombat(RulesPack pack, GameState state, SeatId seat, List<Command> legal)
    {
        if (state.Phase != Phase.Endgame)
        {
            return;
        }
        var me = state.CrewOf(seat);
        if (me.Aboard && !state.Turn.Attacked)
        {
            legal.AddRange(state.Crew.Where(c => c.Seat != seat && c.Aboard && c.At == me.At).Select(c => new Attack(seat, c.Seat.Value)));
            legal.AddRange(Monsters(state).Where(m => m.At == me.At && m.Controller != seat).Select(m => new Attack(seat, m.Id)));
        }
        foreach (var monster in Monsters(state).Where(m => m.Controller == seat && !m.Stunned))
        {
            if (!state.Turn.MonstersMoved.Contains(monster.Id))
            {
                var reach = Reach.From(pack, state.Ship, monster.At!.Value, monster.Stats!.Agility);
                legal.AddRange(state.Ship.Compartments.Where(c => c.Id != monster.At && reach.ContainsKey(c.Id))
                    .Select(c => new MonsterMove(seat, monster.Id, c.Id)));
            }
            if (!state.Turn.MonstersAttacked.Contains(monster.Id))
            {
                legal.AddRange(state.Crew.Where(c => c.Aboard && c.Seat != seat && c.At == monster.At)
                    .Select(c => new MonsterAttack(seat, monster.Id, c.Seat)));
            }
        }
        if (!me.Alive && !state.Turn.Lent)
        {
            legal.AddRange(state.Crew.Where(c => c.Aboard && c.Seat != seat).Select(c => new Lend(seat, c.Seat)));
        }
    }

    private void AttackTarget(Attack attack)
    {
        State = State with { Turn = State.Turn with { Attacked = true } };
        var attacker = State.CrewOf(attack.From);
        var aDice = Rules.Value(Pack, attacker, Trait.Brawn) + ExtraDiceFor(Pack, attacker, Trait.Brawn);
        if (State.Board!.Tokens.FirstOrDefault(t => t.Id == attack.Target) is { } monster)
        {
            Fight(attack.From.Value, aDice, monster.Id, monster.Stats!.Brawn, onAttackerWins: _ =>
            {
                State = State.WithBoard(State.Board!.WithToken(monster with { Stunned = true }));
                return "stunned";
            }, onDefenderWins: () => State = State with { MovesLeft = 0 });
            return;
        }
        var defender = State.CrewOf(new SeatId(attack.Target));
        var dDice = Rules.Value(Pack, defender, Trait.Brawn) + ExtraDiceFor(Pack, defender, Trait.Brawn);
        Fight(attack.From.Value, aDice, attack.Target, dDice, onAttackerWins: margin =>
        {
            State = State with { Waiting = new AllocatePending(defender.Seat, TraitGroup.Physical, margin, attack.From) };
            return "hit";
        }, onDefenderWins: () => State = State with { MovesLeft = 0 });
    }

    private void MonsterAttacks(MonsterAttack attack)
    {
        var monster = State.Board!.Tokens.First(t => t.Id == attack.Monster);
        State = State with { Turn = State.Turn with { MonstersAttacked = State.Turn.MonstersAttacked.Add(monster.Id) } };
        var defender = State.CrewOf(attack.Target);
        var dDice = Rules.Value(Pack, defender, Trait.Brawn) + ExtraDiceFor(Pack, defender, Trait.Brawn);
        Fight(monster.Id, monster.Stats!.Brawn, defender.Seat.Value, dDice, onAttackerWins: margin =>
        {
            State = State with { Waiting = new AllocatePending(defender.Seat, TraitGroup.Physical, margin, attack.From) };
            return "hit";
        }, onDefenderWins: () => State = State with
        {
            Turn = State.Turn with { MonstersMoved = State.Turn.MonstersMoved.Add(monster.Id) },
        });
    }

    /// <summary>
    /// Both sides roll their dice. A higher attacker total wins by the difference; a higher defender total staggers the
    /// attacker; a tie does nothing (spec §2.7).
    /// </summary>
    private void Fight(string attacker, int aDice, string defender, int dDice, Func<int, string> onAttackerWins, Action onDefenderWins)
    {
        var (aRoll, rng) = SignalDice.Roll(State.Rng, aDice, R);
        var (dRoll, next) = SignalDice.Roll(rng, dDice, R);
        State = State with { Rng = next };
        string result;
        if (aRoll.Total > dRoll.Total)
        {
            result = onAttackerWins(aRoll.Total - dRoll.Total);
        }
        else if (dRoll.Total > aRoll.Total)
        {
            onDefenderWins();
            result = "staggered";
        }
        else
        {
            result = "tie";
        }
        Events.Add(new Attacked(attacker, defender, aRoll, dRoll, result));
    }

    private void MoveMonster(MonsterMove move)
    {
        var monster = State.Board!.Tokens.First(t => t.Id == move.Monster);
        State = State.WithBoard(State.Board.WithToken(monster with { At = move.To })) with
        {
            Turn = State.Turn with { MonstersMoved = State.Turn.MonstersMoved.Add(monster.Id) },
        };
        Events.Add(new MonsterMoved(monster.Id, monster.At!.Value, move.To));
    }

    /// <summary>The steps a timeout takes off the group's first trait: half, with the odd step off the higher trait.</summary>
    private static int DefaultSplit(GameState state, AllocatePending a)
    {
        var (first, second) = (a.Group.Members()[0], a.Group.Members()[^1]);
        var steps = state.CrewOf(a.From).Steps;
        return a.Steps / 2 + (a.Steps % 2 == 1 && steps[first] >= steps[second] ? 1 : 0);
    }

    private void AllocateDamage(int first)
    {
        var allocate = (AllocatePending)State.Waiting!;
        State = State with { Waiting = null };
        var members = allocate.Group.Members();
        var crew = State.CrewOf(allocate.From);
        var steps = crew.Steps;
        var dead = false;
        foreach (var (trait, lost) in new[] { (members[0], first), (members[^1], allocate.Steps - first) })
        {
            if (lost == 0)
            {
                continue;
            }
            var before = steps[trait];
            var after = before - lost;
            dead |= after < 1;
            steps = steps.With(trait, Math.Max(0, after));
            Events.Add(new TraitChanged(allocate.From, trait, before, steps[trait]));
        }
        State = State.WithCrew(crew with { Steps = steps });
        if (allocate.By is { } by && by != allocate.From)
        {
            State = State.CountFor(by, s => s with { Damage = s.Damage + allocate.Steps });
        }
        if (dead)
        {
            Kill(allocate.From);
        }
    }

    private void LendDie(Lend lend)
    {
        var target = State.CrewOf(lend.To);
        State = State.WithCrew(target with { Lent = target.Lent + 1 }) with { Turn = State.Turn with { Lent = true } };
        Events.Add(new DieLent(lend.From, lend.To));
    }
}
