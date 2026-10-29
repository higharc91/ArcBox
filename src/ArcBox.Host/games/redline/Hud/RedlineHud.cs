using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation.Hud;
using Board = Redline.Presentation.Hud.DriverSelectBoard;
using Result = Redline.Presentation.Hud.RoundResult;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// The broadcast HUD over the 3D race (plan Task 6, spec §4.8 HUD, mockups 03–05, 09–11, 14–15). <see cref="RedlineScene"/>
/// owns one, calls <see cref="Render"/> with every new host view and <see cref="UpdateTags"/> every frame with the live
/// camera and cars. Everything shown is derived from the view (wording and ordering in <c>Redline.Presentation.Hud</c>);
/// the layout is designed on the 1920×1080 base the window scales from (<c>canvas_items</c> stretch).
/// </summary>
public partial class RedlineHud : CanvasLayer
{
    /// <summary>The Reveal lower-third comes in this long after the view and leaves with the follow-up cut.</summary>
    public const double ResultIn = 0.45;

    private readonly Control _root = new() { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly NameTags _tags = new();
    private readonly BrandSlab _brand = new("REDLINE", 46) { Name = "Brand" };
    private readonly QuestionPanel _header = new();
    private readonly TimingTower _tower = new();
    private readonly TrackMap _map = new();
    private readonly AnswerRow _answers = new();
    private readonly GridStrip _strip = new();
    private readonly RoundResult _result = new();
    private readonly PoleCard _pole = new();
    private readonly DriverSelectBoard _board = new();
    private readonly ClaimLowerThird _claim = new();
    private readonly ChequeredScreen _chequered = new();
    private readonly PodiumHud _podium = new();

    private RedlineHostView? _view;
    private ClaimCard? _claimCard;
    private double _time;
    private double _phaseStart;
    private double? _remaining;
    private double _phaseSeconds = 1;

    /// <summary>Where the podium set stands, for the podium labels (set by the scene when the world is built).</summary>
    public global::Redline.Presentation.Ceremony.PodiumLayout? PodiumLayout { get; set; }

    public RedlineHud()
    {
        Name = "RedlineHud";
        Layer = 10;
    }

    public override void _Ready()
    {
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.Theme = ArcTheme.Theme;
        _root.AddChild(Vignette());
        _root.AddChild(_tags);
        _tags.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        const float mx = HudStyle.MarginX, my = HudStyle.MarginY, gap = HudStyle.Gap;
        var top = my + HudStyle.HeaderHeight + gap;

        Place(_header, Control.LayoutPreset.TopWide, mx, my, -mx, my + HudStyle.HeaderHeight);
        Place(_tower, Control.LayoutPreset.TopLeft, mx, top, mx + TimingTower.Width, top + _tower.FullHeight(22));
        Place(_map, Control.LayoutPreset.TopRight, -mx - TrackMap.MapWidth, top, -mx, top + TrackMap.HeadHeight + TrackMap.MapHeight);
        Place(_answers, Control.LayoutPreset.BottomWide, mx, -my - HudStyle.BottomBarHeight, -mx, -my);
        Place(_strip, Control.LayoutPreset.BottomWide, mx, -my - HudStyle.BottomBarHeight, -mx, -my);
        var resultLeft = mx + TimingTower.Width + gap;
        Place(_result, Control.LayoutPreset.BottomWide, resultLeft, -my - HudStyle.BottomBarHeight - gap - RoundResult.Height,
            -mx, -my - HudStyle.BottomBarHeight - gap);
        Place(_pole, Control.LayoutPreset.BottomWide, resultLeft, -my - HudStyle.BottomBarHeight - gap - PoleCard.Height,
            -mx, -my - HudStyle.BottomBarHeight - gap);
        Place(_board, Control.LayoutPreset.RightWide, -mx - DriverSelectBoard.BoardWidth, my, -mx, -my);
        Place(_claim, Control.LayoutPreset.BottomLeft, mx, -my - ClaimLowerThird.Height, mx + 1920 - (2 * mx) - DriverSelectBoard.BoardWidth - gap, -my);
        Place(_brand, Control.LayoutPreset.TopLeft, mx, my, mx + 260, my + HudStyle.HeaderHeight);
        Place(_chequered, Control.LayoutPreset.FullRect, mx, my, -mx, -my);
        Place(_podium, Control.LayoutPreset.FullRect, mx, my, -mx, -my);

        // A two-line question can make the header a few pixels taller than planned: keep the panels below it.
        _header.Resized += () =>
        {
            var below = Mathf.Max(top, _header.OffsetTop + _header.Size.Y + gap);
            foreach (var c in new Control[] { _tower, _map })
            {
                var h = c.OffsetBottom - c.OffsetTop;
                c.OffsetTop = below;
                c.OffsetBottom = below + h;
            }
        };

        ShowOnly();
    }

    private void Place(Control c, Control.LayoutPreset preset, float left, float top, float right, float bottom)
    {
        _root.AddChild(c);
        c.SetAnchorsPreset(preset);
        c.OffsetLeft = left;
        c.OffsetTop = top;
        c.OffsetRight = right;
        c.OffsetBottom = bottom;
    }

    /// <summary>hud3d.css <c>.tv3d::after</c>: a soft dark vignette that lifts the panels off the picture.</summary>
    private static TextureRect Vignette()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0, 0, 0, 0));
        gradient.SetOffset(0, 0.6f);
        gradient.SetColor(1, new Color(0, 0, 0, 0.42f));
        gradient.SetOffset(1, 1f);
        var texture = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.55f, 0.5f),
            FillTo = new Vector2(0.55f + 0.74f, 0.5f),
            Width = 256,
            Height = 144,
        };
        var rect = new TextureRect
        {
            Name = "Vignette",
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    private void ShowOnly(params Control[] visible)
    {
        foreach (var c in new Control[] { _brand, _header, _tower, _map, _answers, _strip, _result, _pole, _board, _claim, _chequered, _podium })
        {
            c.Visible = visible.Contains(c);
        }
    }

    /// <summary>A new host view arrived (<paramref name="previous"/> is the one rendered before it, if any).</summary>
    public virtual void Render(RedlineHostView view, RedlineHostView? previous)
    {
        var newPhase = previous is null || previous.Phase != view.Phase || previous.Round != view.Round;
        if (newPhase)
        {
            _phaseStart = _time;
        }
        _view = view;

        // The clock: time left read once against the local clock, clamped to the phase length, then run on frame time.
        _phaseSeconds = HudClock.PhaseSeconds(view);
        _remaining = HudClock.Remaining(view.DeadlineUtc, DateTimeOffset.UtcNow, _phaseSeconds);

        _tags.Show(view);
        switch (view.Phase)
        {
            case RacePhase.DriverSelect:
                ShowOnly(_brand, _board, _claim);
                _board.Show(view);
                _claimCard = Board.Claim(view, previous, _claimCard);
                _claim.Card = _claimCard;
                _claim.Visible = _claimCard is not null;
                break;

            case RacePhase.Grid:
                ShowOnly(_header, _tower, _map, _strip);
                _header.Show(HudText.Header(view), lights: true);
                _tower.Show(view);
                _map.Show(view);
                _strip.Show(view);
                break;

            case RacePhase.Throttle:
                ShowOnly(_header, _tower, _map, _strip);
                _header.Show(HudText.Header(view), lights: false);
                _tower.Show(view);
                _map.Show(view);
                _strip.Show(view);
                break;

            case RacePhase.Reading:
            case RacePhase.Question:
            case RacePhase.Reveal:
                // Reading: the question and its answers while it is read out, with LISTEN in place of the clock.
                ShowOnly(_header, _tower, _map, _answers, _result);
                _header.Show(HudText.Header(view), lights: false);
                _tower.Show(view);
                _map.Show(view);
                _answers.Show(view);
                if (view.Phase == RacePhase.Reveal && newPhase)
                {
                    var follow = RedlineScene.FollowUpSeconds;
                    _result.Play(Result.For(view), ResultIn, follow - 0.1);
                }
                else if (view.Phase != RacePhase.Reveal)
                {
                    _result.Stop();
                }
                break;

            case RacePhase.QualifyingReading:
            case RacePhase.QualifyingQuestion:
            case RacePhase.QualifyingResult:
                // Qualifying spec §6: the timing board, the question and its answers; no track map (every car is on the grid).
                // Reading: the question alone, as the answers arrive with the phones' lap clocks (spec §2).
                if (view.Phase == RacePhase.QualifyingReading)
                {
                    ShowOnly(_header, _tower, _pole);
                }
                else
                {
                    ShowOnly(_header, _tower, _answers, _pole);
                }
                _header.Show(HudText.Header(view), lights: false);
                _tower.Show(view);
                _answers.Show(view);
                if (view.Phase == RacePhase.QualifyingResult && newPhase)
                {
                    PoleCards(view, previous);
                }
                else if (view.Phase != RacePhase.QualifyingResult)
                {
                    _pole.Stop();
                }
                break;

            case RacePhase.Anthem:
            case RacePhase.Champagne:
                // The podium: its own labels over the steps, so the car tags stand down.
                ShowOnly(_brand, _podium);
                _podium.Show(view);
                _tags.Clear();
                break;

            default:
                ShowOnly(_chequered);
                _chequered.Show(view);
                break;
        }
        if (view.Phase != RacePhase.DriverSelect)
        {
            _claimCard = null;
        }
        Tick(0);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        Tick(delta);
    }

    /// <summary>
    /// A lap's Result: after the last lap the pole card for the rest of the Result; otherwise the provisional pole card when
    /// the leader changed. The name is upper-cased for the card; the team is P1's.
    /// </summary>
    private void PoleCards(RedlineHostView view, RedlineHostView? previous)
    {
        _pole.Stop();
        if (view.Qualifying is not { } session)
        {
            return;
        }
        var p1 = view.Drivers.FirstOrDefault(d => d.Code == session.Rows.MinBy(r => r.Position)?.Code);
        var team = TeamAccent.Team(p1?.TeamId)?.Name.ToUpperInvariant() ?? "";
        var accent = HudStyle.Hex(TeamAccent.ById(p1?.TeamId));
        if (session.Lap >= session.Laps)
        {
            if (QualifyingText.Pole(view) is { } pole)
            {
                _pole.PlayPole(pole.Name.ToUpperInvariant(), team, accent, pole.Total, ResultIn, _phaseSeconds - 0.2);
            }
        }
        else if (QualifyingText.ProvisionalPole(view, previous) is { } name)
        {
            _pole.PlayProvisional(name.ToUpperInvariant(), team, accent, ResultIn);
        }
    }

    private void Tick(double delta)
    {
        if (_view is null)
        {
            return;
        }
        if (_remaining is { } r)
        {
            _remaining = Math.Max(0, r - delta);
        }
        var fraction = _remaining is { } left ? HudClock.Fraction(left, _phaseSeconds) : 0;
        _header.SetClock(_remaining, fraction);
        _header.SetLights(StartLights.Lit(_view.Phase, _time - _phaseStart));
        _board.SetClock(_remaining is { } s ? HudClock.Format(s) : "");
    }

    /// <summary>Per frame: project name tags for the player cars through <paramref name="camera"/>; move the map dots.</summary>
    public virtual void UpdateTags(Camera3D camera, IReadOnlyDictionary<string, Node3D> cars)
    {
        if (_view is null || !IsInsideTree())
        {
            return;
        }
        if (_podium.Visible)
        {
            if (PodiumLayout is { } layout)
            {
                _podium.Track(camera, layout);
            }
            return;
        }
        _map.Track(cars);
        _tags.Update(camera, cars, Occluders());
    }

    /// <summary>Panels on screen now, in canvas pixels: a tag whose anchor or body falls on one is hidden.</summary>
    private List<ScreenRect> Occluders()
    {
        var rects = new List<Rect2>();
        foreach (var c in new Control[] { _brand, _header, _tower, _map, _answers, _strip, _board, _claim })
        {
            if (c.Visible)
            {
                rects.Add(c.GetGlobalRect());
            }
        }
        if (_result.Visible && _result.Footprint is { Size.X: > 0 } footprint)
        {
            rects.Add(footprint);
        }
        if (_pole.Visible && _pole.Footprint is { Size.X: > 0 } card)
        {
            rects.Add(card);
        }
        if (_chequered.Visible)
        {
            rects.AddRange(_chequered.Panels());
        }
        return [.. rects.Select(r => new ScreenRect(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y))];
    }
}
