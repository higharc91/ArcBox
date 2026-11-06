using System.Diagnostics;
using System.Text.Json;
using ArcBox.Host.Shell;
using ArcBox.Sdk;
using Godot;
using Tactics.Game;
using Tactics.Presentation.Beats;

namespace ArcBox.Host.Games.Tactics;

/// <summary>Server-free showcase through the normal Tactics scene; advances only on its real shown signal.</summary>
public partial class FxDemoFeeder : Node
{
    private readonly Action<JsonElement> _send = _ => { };
    private readonly Screenshotter? _shots;
    private readonly IReadOnlyList<FxShowcaseSample> _samples = [];
    private readonly double[] _tails = [];
    private readonly List<object> _measurements = [];
    private readonly List<double> _frameMs = [];
    private int _sample = -1;
    private int _frame;
    private double _clock;
    private double _afterAt = double.PositiveInfinity;
    private double _endAt = double.PositiveInfinity;
    private double _began;
    private double _captureAt;
    private double _shownAt;
    private long _previous;
    private bool _playing;
    private bool _finished;

    public FxDemoFeeder() { }
    public FxDemoFeeder(Action<JsonElement> send, Screenshotter? shots, IReadOnlyList<FxShowcaseSample> samples, FxArt? art = null)
    {
        (_send, _shots, _samples) = (send, shots, samples);
        if (samples.Count == 0) throw new ArgumentException("The showcase needs samples.", nameof(samples));
        _tails = [.. samples.Select(s => art is null ? .85 : FxShowcase.TailFor(s, art))];
    }
    public void Signal(string kind, object payload)
    {
        if (kind != "shown" || !_playing) return;
        var seq = JsonSerializer.SerializeToElement(payload, ArcJson.Options).GetProperty("seq").GetInt32();
        if (seq != _sample + 1) return;
        _shownAt = _clock;
        _endAt = _clock + _tails[_sample];
    }
    private void Send(BattleView battle, FxShowcaseSample sample)
        => _send(JsonSerializer.SerializeToElement(new TacticsHostView("Battle", sample.Name, null, sample.Level, [], null, battle, null, null), ArcJson.Options));

    public override void _Process(double delta)
    {
        if (_finished) return;
        var stamp = Stopwatch.GetTimestamp();
        if (_previous != 0 && _playing && _frame > 10) _frameMs.Add(Stopwatch.GetElapsedTime(_previous, stamp).TotalMilliseconds);
        _previous = stamp;
        _clock += delta;
        if (_sample == -1 || _clock >= _endAt + 0.2)
        {
            if (_sample >= 0)
            {
                var ordered = _frameMs.Order().ToArray();
                var sample = _samples[_sample];
                _measurements.Add(new { sample.Id, sample.Name, sample.Description, sample.Level, Seconds = _shownAt - _began,
                    Frames = _frame, FrameMedianMs = ordered.Length == 0 ? 0 : ordered[ordered.Length / 2],
                    TextureBytes = (long)Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) });
            }
            if (++_sample >= _samples.Count)
            {
                _finished = true;
                if (_shots is not null) File.WriteAllText(Path.Combine(_shots.Dir, "showcase.json"), JsonSerializer.Serialize(_measurements, new JsonSerializerOptions(ArcJson.Options) { WriteIndented = true }));
                GD.Print("FX DEMO complete");
                GetTree().CreateTimer(.25).Timeout += () => GetTree().Quit();
                return;
            }
            _playing = false;
            _frameMs.Clear();
            _frame = 0;
            _endAt = double.PositiveInfinity;
            _afterAt = _clock + .5;
            Send(_samples[_sample].Before, _samples[_sample]);
        }
        if (!_playing && _clock >= _afterAt)
        {
            _playing = true;
            _began = _clock;
            _captureAt = _clock;
            Send(_samples[_sample].After, _samples[_sample]);
            GD.Print($"FX DEMO {_samples[_sample].Id}");
        }
        if (_playing && _clock < _endAt && _clock >= _captureAt)
        {
            if (System.Environment.GetEnvironmentVariable("ARCBOX_FX_CAPTURE") != "0")
                _shots?.Capture($"{_samples[_sample].Id}-frame{_frame:0000}");
            _frame++;
            _captureAt += 1.0 / 30;
        }
        if (_playing && _clock > _began + 20 && double.IsPositiveInfinity(_endAt))
            throw new InvalidOperationException($"No shown acknowledgement for {_samples[_sample].Id}.");
    }
}
