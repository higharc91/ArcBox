using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Campaigns;

namespace ArcBox.Server.Rooms;

// Campaign rooms (spec §3.2): a game whose manifest sets UsesCampaigns gets a campaign step in the lobby, every member
// of the campaign is a seat, and the room holds the campaign's lease while the game runs.
public sealed partial class Room
{
    // Selected in the lobby, and kept across games until another game is selected. Only the actor touches it.
    private string? _campaignId;

    // The campaign checked out for the running game. ForceClose releases it from outside the actor, so it is only ever
    // taken with Interlocked.Exchange: whoever takes it releases it, exactly once.
    private string? _leasedCampaignId;

    /// <summary>Selects one of the selected game's campaigns. The host or the VIP.</summary>
    public Task SelectCampaignAsync(string connectionId, string campaignId) => EnqueueAsync(() =>
    {
        EnsureHostOrVip(connectionId);
        EnsureCampaignLobby();

        var info = _campaigns.Find(campaignId);
        if (info is null || info.GameId != _selectedGameId)
        {
            throw new RoomException("Campaign not found.");
        }

        if (_campaigns.LeasedBy(info.Id) is { } holder && holder != Code)
        {
            throw new RoomException($"{info.Name} is being played in room {holder}.");
        }

        if (info.Members.Count > _options.MaxPlayers)
        {
            throw new RoomException($"This campaign has {info.Members.Count} members; rooms hold {_options.MaxPlayers}.");
        }

        _campaignId = info.Id;
        BroadcastLobby();
        Save();
    });

    /// <summary>
    /// The VIP creates a campaign for the selected game, as its owner, and the room selects it. The VIP's seat gets a
    /// profile if it has none. Always returns a fresh key for that profile (ruling R3).
    /// </summary>
    public Task<ProfileKey> CreateCampaignAsync(string connectionId, string name) => EnqueueAsync(() =>
    {
        var vip = _members.VipId is { } vipId ? _members.Find(vipId) : null;
        if (vip is null || vip.ConnectionId != connectionId)
        {
            throw new RoomException("Only the VIP can create a campaign.");
        }

        EnsureCampaignLobby();

        // Checked here as well as by the service, so a bad name doesn't leave a new profile behind.
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 or > CampaignService.NameMaxLength)
        {
            throw new RoomException("Campaign names are 1–32 characters.");
        }

        var owner = (vip.Profile is { } id ? _profiles.Find(id) : null) ?? _profiles.Create(vip.Name);
        var info = _campaigns.Create(_selectedGameId!, trimmed, owner);

        vip.Profile = owner.Id;
        _campaignId = info.Id;
        BroadcastLobby();
        Save();
        return _profiles.IssueKey(owner.Id);
    });

    /// <summary>
    /// A phone already in the lobby answers "Who are you in {campaign}?": an existing member (by member or by that
    /// member's offline seat), or null for "I'm new here" (under <see cref="ClaimMemberRequest.NewName"/> or the seat's own
    /// name). Its own seat takes the member's profile and name. Returns a
    /// new key, or null when one of <see cref="ClaimMemberRequest.ProfileKeys"/> already belongs to that profile.
    /// </summary>
    public Task<ProfileKey?> ClaimMemberAsync(string connectionId, ClaimMemberRequest request) => EnqueueAsync(() =>
    {
        if (_status == RoomStatus.InGame)
        {
            throw new RoomException("You can only change who you are in the lobby.");
        }

        var campaign = SelectedCampaign() ?? throw new RoomException("Pick a campaign first.");
        var seat = request.Member is { } member ? OneSeat(member) : null;
        if (seat is not null && request.NewName is not null)
        {
            throw new RoomException("Pick a seat or enter a name.");
        }

        var slot = _members.FindByConnection(connectionId) ?? throw new RoomException("Couldn't rejoin. Please join again.");
        var keys = request.ProfileKeys ?? [];

        var key = seat is null
            ? ClaimNewMember(slot, campaign, keys, request.NewName)
            : ClaimExistingMember(slot, seat, campaign, keys);
        BroadcastLobby();
        Save();
        return key;
    });

    private ProfileKey? ClaimExistingMember(PlayerSlot slot, SeatRef seat, CampaignView campaign, IReadOnlyList<string> keys)
    {
        PlayerSlot? held;
        ProfileId member;
        if (seat.Member is { } id)
        {
            member = campaign.Has(id) ? id : throw new RoomException("Couldn't rejoin. Please join again.");
            held = _members.FindByProfile(member);
        }
        else
        {
            held = _members.Find(seat.Slot!.Value);
            member = held?.Profile is { } p && campaign.Has(p) ? p : throw new RoomException("Couldn't rejoin. Please join again.");
        }

        if (held == slot)
        {
            return KeyFor(member, keys); // the caller is already this member
        }

        if (held is { Connected: true })
        {
            throw new RoomException($"Someone's already playing as {campaign.Card(member).Title}.");
        }

        // The caller's seat takes the member's name, which no other seat may have (the member's own seat goes).
        if (_members.CheckName(campaign.Member(member).DisplayName, out _, slot, held) is { } error)
        {
            throw new RoomException(error);
        }

        // The member's offline seat goes: the person is here now, in the caller's seat.
        if (held is not null)
        {
            _members.Remove(held.Id);
        }

        slot.Profile = member;
        slot.Name = campaign.Member(member).DisplayName;
        return KeyFor(member, keys);
    }

    /// <summary>
    /// "I'm new here" from a seated phone: a new member under <paramref name="newName"/>, or without it the seat's own
    /// name, which the seat takes. A seat that already has a profile from another campaign is the same person, so it
    /// joins this one as itself unless it asks for a different name.
    /// </summary>
    private ProfileKey? ClaimNewMember(PlayerSlot slot, CampaignView campaign, IReadOnlyList<string> keys, string? newName)
    {
        var existing = slot.Profile is { } id && !campaign.Has(id) ? _profiles.Find(id) : null;
        if (_members.CheckName(newName ?? existing?.DisplayName ?? slot.Name, out var name, slot) is { } error)
        {
            throw new RoomException(error);
        }

        EnsureCanJoin(campaign, name);

        var reuse = existing is not null && existing.DisplayName == name;
        var profile = reuse ? existing! : _profiles.Create(name);
        _campaigns.AddMember(campaign.Info.Id, profile);
        slot.Profile = profile.Id;
        slot.Name = profile.DisplayName;
        return reuse ? KeyFor(profile.Id, keys) : _profiles.IssueKey(profile.Id);
    }

    /// <summary>"I'm new here" in a campaign lobby: a new profile, a membership and a seat.</summary>
    private JoinResult TakeNewMemberSeat(string connectionId, string rawName, CampaignView campaign)
    {
        var replacing = SeatToReplace(connectionId, campaign);
        if (_members.CheckNew(rawName, out var name, replacing) is { } error)
        {
            throw new RoomException(error);
        }

        EnsureCanJoin(campaign, name);

        var profile = _profiles.Create(name);
        _campaigns.AddMember(campaign.Info.Id, profile);
        LeaveSeat(connectionId, replacing);
        var slot = _members.Add(name, connectionId, profile.Id, out var secret);
        return Seated(connectionId, slot, secret, _profiles.IssueKey(profile.Id));
    }

    /// <summary>
    /// A campaign member picked on the "Who are you?" grid. Their seat, if they have one, is taken like any offline
    /// seat; in the lobby a member without one gets a new seat under their name.
    /// </summary>
    private JoinResult TakeMemberSeat(string connectionId, ProfileId member, IReadOnlyList<string> keys)
    {
        var campaign = SelectedCampaign();
        if (campaign is null || !campaign.Has(member))
        {
            throw new RoomException("Couldn't rejoin. Please join again.");
        }

        if (_members.FindByProfile(member) is { } seat)
        {
            return TakeOfflineSeat(connectionId, seat.Id, keys);
        }

        if (_status == RoomStatus.InGame)
        {
            throw new RoomException("Game in progress, join after this round.");
        }

        var replacing = SeatToReplace(connectionId, campaign);
        if (_members.CheckNew(campaign.Member(member).DisplayName, out var name, replacing) is { } error)
        {
            throw new RoomException(error);
        }

        LeaveSeat(connectionId, replacing);
        var slot = _members.Add(name, connectionId, member, out var secret);
        return Seated(connectionId, slot, secret, KeyFor(member, keys));
    }

    /// <summary>
    /// The seat a phone gives up for good by taking a new one in a campaign lobby: the one it holds, unless that is a
    /// member's seat. It counts against neither the new seat's name nor the room size.
    /// </summary>
    private PlayerSlot? SeatToReplace(string connectionId, CampaignView campaign) =>
        _members.FindByConnection(connectionId) is { } current && !campaign.Has(current.Profile) ? current : null;

    /// <summary>
    /// A phone that holds a seat moves to a new one (ruling R9), leaving the old one through <see cref="Unseat"/> as a
    /// lost connection would. A member's seat stays, offline, for that member; any other seat would only be dropped at
    /// Start, so <paramref name="replacing"/> goes now.
    /// </summary>
    private void LeaveSeat(string connectionId, PlayerSlot? replacing)
    {
        Unseat(connectionId);
        if (replacing is not null)
        {
            _members.Remove(replacing.Id);
        }
    }

    /// <summary>
    /// Checks the campaign out for the game about to start, then makes the room's seats match its members: a seat
    /// left offline by someone who never picked who they are goes, and every member without a seat gets an offline one
    /// (its secret is never handed out; a phone takes it through TakeSeat). Every seat offline now is absent.
    /// </summary>
    private CampaignLease CheckOutForStart(CampaignView campaign)
    {
        var id = campaign.Info.Id;
        var lease = _campaigns.CheckOut(id, Code)
            ?? throw new RoomException($"{campaign.Info.Name} is being played in room {_campaigns.LeasedBy(id)}.");
        Interlocked.Exchange(ref _leasedCampaignId, id);

        foreach (var stray in _members.All.Where(s => !s.Connected && !campaign.Has(s.Profile)).ToList())
        {
            _members.Remove(stray.Id);
        }

        foreach (var member in campaign.Info.Members.Where(m => _members.FindByProfile(m.Profile) is null))
        {
            _members.Add(member.DisplayName, connectionId: null, member.Profile, out _);
        }

        foreach (var slot in _members.All)
        {
            slot.AbsentAtStart = !slot.Connected;
        }

        return lease;
    }

    /// <summary>Gives the campaign back if a game had it. Safe from any thread, and only the first call releases.</summary>
    private void ReleaseCampaign()
    {
        if (Interlocked.Exchange(ref _leasedCampaignId, null) is { } id)
        {
            _campaigns.Release(id, Code);
        }
    }

    /// <summary>Members with no connected seat, in member order: their offline seat if they have one, else themselves.</summary>
    private List<Offer> CampaignOffers(CampaignView campaign, bool inGame)
    {
        var offers = new List<Offer>();
        foreach (var member in campaign.Info.Members)
        {
            var slot = _members.FindByProfile(member.Profile);
            if (slot is { Connected: true } || (slot is null && inGame))
            {
                continue; // seated, or not a player in the running game
            }

            var seat = slot is null ? new SeatRef(Member: member.Profile) : new SeatRef(Slot: slot.Id);
            offers.Add(new Offer(
                new SeatOffer(seat, campaign.Card(member.Profile), Absent: inGame && slot is { AbsentAtStart: true }),
                member.Profile));
        }

        return offers;
    }

    /// <summary>The first of this phone's profiles, most recently used first, that is on offer.</summary>
    private SeatOffer? Suggest(List<Offer> offers, IReadOnlyList<string> profileKeys)
    {
        if (offers.Count == 0 || profileKeys.Count == 0)
        {
            return null;
        }

        return _profiles.ResolveAll(profileKeys)
            .Select(p => offers.FirstOrDefault(o => o.Profile == p.Id)?.Seat)
            .FirstOrDefault(o => o is not null);
    }

    private IReadOnlyList<RosterEntry> Roster(CampaignView campaign) =>
    [
        .. campaign.Info.Members.Select(m =>
        {
            var slot = _members.FindByProfile(m.Profile);
            return new RosterEntry(m.Profile, campaign.Card(m.Profile), slot?.Id, slot?.Connected ?? false);
        })
    ];

    /// <summary>
    /// A key for <paramref name="profile"/> on this phone, or null when it has none or the phone already holds one
    /// (which then counts as a use, so it is the one suggested next time).
    /// </summary>
    private ProfileKey? KeyFor(ProfileId? profile, IReadOnlyList<string> profileKeys)
    {
        if (profile is not { } id)
        {
            return null;
        }

        var held = profileKeys.FirstOrDefault(k => _profiles.ResolveAll([k]) is [var p] && p.Id == id);
        if (held is not null)
        {
            _profiles.Resolve(held);
            return null;
        }

        return _profiles.IssueKey(id);
    }

    /// <summary>How a seat is named to the player: the game's card for a campaign member, otherwise the seat's name.</summary>
    private string TitleOf(PlayerSlot slot) =>
        SelectedCampaign() is { } campaign && slot.Profile is { } member && campaign.Has(member)
            ? campaign.Card(member).Title
            : slot.Name;

    /// <summary>The selected campaign, read now: in a game it is the leased campaign's current document.</summary>
    private CampaignView? SelectedCampaign()
    {
        if (_campaignId is null || _campaigns.Find(_campaignId) is not { } info || _rules.For(info.GameId) is not { } rules)
        {
            return null;
        }

        return new CampaignView(info, _campaigns.DocumentOf(info.Id), rules);
    }

    private void EnsureCampaignLobby()
    {
        if (_status == RoomStatus.InGame)
        {
            throw new RoomException("A game is already running.");
        }

        if (SelectedManifest is not { } manifest)
        {
            throw new RoomException("Pick a game first.");
        }

        if (!manifest.UsesCampaigns || _rules.For(manifest.Id) is null)
        {
            throw new RoomException("This game doesn't use campaigns.");
        }
    }

    // Checked before a profile is created, so a refusal leaves nothing behind.
    private void EnsureCanJoin(CampaignView campaign, string name)
    {
        // A campaign another room is playing takes no new members: they would miss that session (ruling R14).
        if (_campaigns.LeasedBy(campaign.Info.Id) is { } holder && holder != Code)
        {
            throw new RoomException($"{campaign.Info.Name} is being played in room {holder}.");
        }

        if (campaign.Info.Members.Count >= CampaignService.MaxMembers)
        {
            throw new RoomException("This campaign is full (8 members).");
        }

        if (campaign.Info.Members.Any(m => string.Equals(m.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new RoomException("That name is taken.");
        }
    }

    /// <summary>A seat is exactly one of a slot or a member.</summary>
    private static SeatRef OneSeat(SeatRef seat) =>
        (seat.Slot is null) == (seat.Member is null) ? throw new RoomException("Pick a seat or enter a name.") : seat;

    /// <summary>An offer, with the profile behind it for <see cref="Suggest"/>.</summary>
    private sealed record Offer(SeatOffer Seat, ProfileId? Profile);

    /// <summary>A campaign as the room sees it for one message: its info, current document and the game's rules.</summary>
    private sealed record CampaignView(CampaignInfo Info, JsonElement Document, ICampaignRules Rules)
    {
        public bool Has(ProfileId? profile) => profile is { } id && Info.Members.Any(m => m.Profile == id);

        public CampaignMember Member(ProfileId profile) => Info.Members.First(m => m.Profile == profile);

        public SeatCard Card(ProfileId member) => Rules.CardFor(Info, Document, member);
    }
}
