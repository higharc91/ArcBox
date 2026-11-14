using Meridian.Logic;

namespace Meridian.Game;

// Phone screens (spec §3.4). Each record is one screen's CustomView.Data.

/// <summary>A crew member as the crew-pick and sheet screens show it: values from step 1 to 8, and where they start.</summary>
public sealed record CrewCard(CrewId Id, string Name, string Role, EqArray<TrackCard> Tracks, bool Taken, bool Mine);

public sealed record TrackCard(Trait Trait, EqArray<int> Values, int Step);

/// <summary><c>crewPick</c>: choose a crew member. Sends <c>pick { crew }</c>.</summary>
public sealed record CrewPickScreen(EqArray<CrewCard> Crew, int Picked, int Seats);

/// <summary>Everything the phone needs to draw the decks.</summary>
public sealed record MapData(EqArray<CompartmentCard> Compartments, EqArray<CrewMarker> Crew);

/// <summary>An item as a phone shows it. <see cref="Use"/> is its use's label, or null for a passive-only item.</summary>
public sealed record ItemCard(CardId Id, CardKind Kind, string Title, string Text, string? Use, bool Usable);

public sealed record Reachable(string Id, int Cost);

public sealed record Openable(string At, Side Side);

/// <summary>A crew member in the same compartment, for trading.</summary>
public sealed record Neighbour(string Seat, string Name, EqArray<ItemCard> Items);

/// <summary>
/// <c>turn</c>: the active seat's free choice. Everything on it comes from <see cref="Rules.Legal"/>, so the phone offers
/// exactly what the rules accept. Sends <c>moveTo</c>, <c>open</c>, <c>useItem</c>, <c>useRoom</c>, <c>offerTrade</c>,
/// <c>endTurn</c>.
/// </summary>
/// <remarks>
/// In the endgame it also offers attacks, the scenario's actions and the seat's monsters (whose activation is part of
/// the controller's turn), and carries the seat's briefing.
/// </remarks>
public sealed record TurnScreen(MapData Map, string At, int Moves, EqArray<Reachable> Reach, EqArray<Openable> Opens,
                                EqArray<ItemCard> Items, string? Room, EqArray<Neighbour> Neighbours, CrewCard Me,
                                EqArray<AttackTarget> Attacks = default, EqArray<ScenarioAction> Actions = default,
                                EqArray<MonsterCard> Monsters = default, BriefingCard? Briefing = null);

public sealed record RotationOption(int Rotation, EqArray<Side> Hatches);

/// <summary><c>orient</c>: which way round the new compartment goes. Sends <c>orient { rotation }</c>.</summary>
public sealed record OrientScreen(string Name, Cell At, Side Toward, EqArray<RotationOption> Options, MapData Map);

public sealed record OptionCard(string Id, string Label, string Text, Trait? Check);

/// <summary><c>card</c>: a choice card. Sends <c>choose { option }</c>.</summary>
public sealed record CardScreen(CardId Id, CardKind Kind, string Title, string Text, EqArray<OptionCard> Options);

public sealed record BandCard(int Min, int? Max, string Text);

/// <summary>
/// <c>roll</c>: a check or the Breach roll. Sends <c>roll</c>. <see cref="Needed"/> is set for the Breach roll; a check
/// lists its bands instead.
/// </summary>
public sealed record RollScreen(string Reason, Trait? Trait, int Dice, string Title, string Text, EqArray<BandCard> Bands,
                                int? Needed);

/// <summary><c>trade</c>: someone offers a trade. Sends <c>answerTrade { accept }</c>.</summary>
public sealed record TradeScreen(string From, EqArray<ItemCard> Give, EqArray<ItemCard> Take);

/// <summary><c>sheet</c>: everyone who isn't acting. Tabs are phone-side, so browsing never changes the view.</summary>
public sealed record SheetScreen(CrewCard Me, string At, EqArray<ItemCard> Items, MapData Map, string Active, string? Waiting,
                                 BriefingCard? Briefing = null);

/// <summary><c>watch</c>: the main screen is presenting something.</summary>
public sealed record WatchScreen(Presented Now);

/// <summary><c>over</c>: the game has ended.</summary>
public sealed record OverScreen(string Message);

// The endgame's screens (M3 plan, Task 12). Each is built from Visibility.For(state, seat): a phone sees what its seat sees.

/// <summary>A seat's role and briefing. Every role's briefing has this same shape, so a glance tells nobody anything.</summary>
public sealed record BriefingCard(string Role, string Name, string Briefing, EqArray<string> Objectives);

/// <summary><c>briefing</c>: read it, then Ready. Sends <c>ready</c>.</summary>
public sealed record BriefingScreen(string Title, BriefingCard You, bool Ready, int ReadyCount, int Seats);

/// <summary><c>allocate</c>: split lost steps between two traits. Sends <c>allocate { first }</c>.</summary>
public sealed record AllocateScreen(int Steps, EqArray<TrackCard> Tracks, string? By);

public sealed record VoteOption(string Id, string Label);

/// <summary><c>vote</c>: a secret ballot. Sends <c>vote { choice }</c>. <see cref="Cast"/> once this seat has voted.</summary>
public sealed record VoteScreen(string Prompt, EqArray<VoteOption> Options, bool Cast, int Remaining);

/// <summary><c>echo</c>: a dead crew member's turn. Sends <c>lend { to }</c> and <c>endTurn</c>.</summary>
public sealed record EchoScreen(EqArray<VoteOption> Targets, bool Lent);

public sealed record RoleReveal(string Seat, string Name, string Role, bool Won);
public sealed record AwardCard(string Title, string Seat, string Name);

/// <summary><c>debrief</c>: how it ended, every role revealed, the awards. The VIP sends <c>continue</c>.</summary>
public sealed record DebriefScreen(string Message, bool Won, EqArray<RoleReveal> Roles, EqArray<AwardCard> Awards, bool Vip);

public sealed record AttackTarget(string Id, string Name);

/// <summary>A monster the seat commands, with where it can go and whom it can attack this turn.</summary>
public sealed record MonsterCard(string Id, string Kind, string At, EqArray<string> Moves, EqArray<AttackTarget> Targets);
