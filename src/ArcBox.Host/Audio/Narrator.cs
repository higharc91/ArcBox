using System.Collections.Concurrent;
using System.Diagnostics;
using ArcBox.Host.Core.Audio;
using ArcBox.Sdk.Speech;
using Godot;

namespace ArcBox.Host.Audio;

/// <summary>
/// The platform narrator (audio spec §3.3), shared by every game's TV scene: <see cref="Say"/> a line in a voice theme
/// and it is spoken one at a time on the Voice bus. Lines are fetched from the server's <c>/api/tts</c> (Google Chirp 3:
/// HD) with the session's room code and host token, off the main thread, and played as <see cref="AudioStreamMP3"/>.
/// When the server has no voice (no key, Google down, a timeout after 2.5 s, any error) the line is spoken at once by
/// the TV's built-in TTS with the theme's fallback rate and pitch, and the server is left alone for 60 s
/// (<see cref="NarratorFallbackPolicy"/>). A question cuts off commentary; commentary that has waited 4 s is dropped
/// (<see cref="NarrationQueue"/>). With no built-in voices either (Linux without speech-dispatcher), the narrator is
/// <see cref="NarratorState.Off"/>: lines are only logged, each holding the voice for about as long as it would take to say.
/// </summary>
public partial class Narrator : Node
{
    /// <summary>Speaking pace assumed for silent lines and the built-in voice's safety timeout: about 2.7 words a second, as <c>RedlineRules.ReadingTime</c>.</summary>
    private const double SecondsPerWord = 0.36;

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private enum Mode { Idle, Fetching, Clip, BuiltIn, Silent }

    private readonly NarrationQueue _queue = new();
    private readonly NarratorFallbackPolicy _policy = new();
    private readonly ConcurrentQueue<Action> _main = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TtsClient _client = new(Http);

    private AudioStreamPlayer _player = null!;
    private TtsSession? _session;
    private IReadOnlyDictionary<string, FallbackVoice> _hints = new Dictionary<string, FallbackVoice>();
    private string? _builtInVoice;
    private Mode _mode;
    private int _token;
    private long _utterance;
    private bool _utteranceStarted;
    private double _deadline;
    private CancellationTokenSource? _fetch;
    private NarratorState? _reported;

    /// <summary>Part of the id or name of the built-in voice to prefer (settings <c>narrationVoice</c>); null for the first English one.</summary>
    public string? PreferredVoice { get; set; }

    public NarratorState State => _policy.State(Now);

    /// <summary>The last line started (for the F3 overlay).</summary>
    public string? LastLine { get; private set; }

    /// <summary>True while a voice is audible (a clip, or the built-in voice between its start and end callbacks).</summary>
    public bool Speaking => _mode == Mode.Clip || (_mode == Mode.BuiltIn && _utteranceStarted);

    private double Now => _clock.Elapsed.TotalSeconds;

    public override void _Ready()
    {
        Name = "Narrator";
        _player = new AudioStreamPlayer { Name = "VoicePlayer", Bus = AudioBuses.Voice };
        _player.Finished += () =>
        {
            if (_mode == Mode.Clip)
            {
                Done();
            }
        };
        AddChild(_player);

        _builtInVoice = PickBuiltInVoice(PreferredVoice, out var description);
        _policy.HasBuiltInVoice = _builtInVoice is not null;
        GD.Print(_builtInVoice is null
            ? "NARRATOR no built-in TTS voices (on Linux, install speech-dispatcher); fallback narration is off"
            : $"NARRATOR built-in voice {description}");
        if (_builtInVoice is not null)
        {
            // Utterance callbacks may come from the TTS engine's thread: hand them to the main thread.
            DisplayServer.TtsSetUtteranceCallback(DisplayServer.TtsUtteranceEvent.Started,
                Callable.From<long>(id => _main.Enqueue(() => OnUtterance(id, ended: false))));
            DisplayServer.TtsSetUtteranceCallback(DisplayServer.TtsUtteranceEvent.Ended,
                Callable.From<long>(id => _main.Enqueue(() => OnUtterance(id, ended: true))));
            DisplayServer.TtsSetUtteranceCallback(DisplayServer.TtsUtteranceEvent.Canceled,
                Callable.From<long>(id => _main.Enqueue(() => OnUtterance(id, ended: true))));
        }
        Report();
    }

    /// <summary>
    /// The room whose host credentials authorise <c>/api/tts</c>, or null (no server: built-in voice only). A new room
    /// fetches the themes' fallback hints once and gives the server voice a fresh try.
    /// </summary>
    public void SetSession(TtsSession? session)
    {
        if (session == _session)
        {
            return;
        }
        _session = session;
        _policy.HasServer = session is not null;
        _policy.Succeeded();
        if (session is not null)
        {
            _ = Task.Run(async () =>
            {
                var hints = await _client.VoicesAsync(session, CancellationToken.None);
                _main.Enqueue(() =>
                {
                    if (hints is not null && session == _session)
                    {
                        _hints = hints;
                        GD.Print($"NARRATOR voice themes: {string.Join(", ", hints.Keys.Order())}");
                    }
                });
            });
        }
        Report();
    }

    /// <summary>
    /// Queues <paramref name="text"/> in voice theme <paramref name="theme"/> (e.g. <c>redline.questions</c>). An
    /// <paramref name="opener"/> is a commentary line that a question waits behind instead of cutting off
    /// (<see cref="Narration.Opener"/>).
    /// </summary>
    public void Say(string text, string theme, NarrationPriority priority, bool opener = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        if (_queue.Enqueue(new Narration(text.Trim(), theme, priority, Now, opener)))
        {
            Cut();
        }
        Pump();
    }

    /// <summary>Stops the line playing and forgets everything queued (e.g. the room closed).</summary>
    public void StopAll()
    {
        Cut();
        _queue.Clear();
    }

    public override void _Process(double delta)
    {
        while (_main.TryDequeue(out var action))
        {
            action();
        }
        if (_mode is (Mode.Silent or Mode.BuiltIn) && Now >= _deadline)
        {
            // Off: the line's estimated time is up. Built-in: its end callback never came; don't hold the queue.
            if (_mode == Mode.BuiltIn)
            {
                DisplayServer.TtsStop();
            }
            Done();
        }
        Pump();
        Report();
    }

    public override void _ExitTree()
    {
        _fetch?.Cancel();
        if (_mode == Mode.BuiltIn)
        {
            DisplayServer.TtsStop();
        }
    }

    // ---- Queue ---------------------------------------------------------------------------------------------

    private void Pump()
    {
        if (_mode != Mode.Idle)
        {
            return;
        }
        var dropped = new List<Narration>();
        var line = _queue.Next(Now, dropped);
        foreach (var d in dropped)
        {
            GD.Print($"NARRATOR dropped (waited over {NarrationQueue.StaleSeconds} s): {d.Text}");
        }
        if (line is not null)
        {
            Begin(line);
        }
    }

    private void Begin(Narration line)
    {
        var token = ++_token;
        LastLine = line.Text;
        if (_session is { } session && _policy.UseServer(Now))
        {
            _mode = Mode.Fetching;
            _fetch = new CancellationTokenSource();
            var ct = _fetch.Token;
            _ = Task.Run(async () =>
            {
                var result = await _client.SynthesizeAsync(session, line.Text, line.Theme, ct);
                _main.Enqueue(() => OnFetched(token, line, result));
            }, ct);
            return;
        }
        SpeakBuiltIn(line);
    }

    private void OnFetched(int token, Narration line, TtsResult result)
    {
        if (token != _token || _mode != Mode.Fetching)
        {
            return;   // cut off while it was being fetched
        }
        if (result is TtsAudio audio && Play(audio.Mp3, line))
        {
            _policy.Succeeded();
            return;
        }
        var failed = result as TtsFailed ?? new TtsFailed(TtsFailure.Unavailable);
        _policy.Failed(failed.Failure, Now, failed.RetryAfter);
        var wait = _policy.CoolDownLeft(Now);
        GD.Print($"NARRATOR server voice unavailable ({failed.Failure}{(failed.Status is { } s ? $" {s}" : "")})" +
                 (wait > 0 ? $"; built-in voice for {wait:0} s" : "; built-in voice for this line"));
        SpeakBuiltIn(line);
    }

    private bool Play(byte[] mp3, Narration line)
    {
        var stream = new AudioStreamMP3 { Data = mp3 };
        if (stream.GetLength() <= 0)
        {
            return false;   // not decodable as MP3
        }
        _player.Stream = stream;
        _player.Play();
        _mode = Mode.Clip;
        AudioLog.Play(AudioBuses.Voice, $"{line.Theme}: {line.Text}");
        return true;
    }

    private void SpeakBuiltIn(Narration line)
    {
        AudioLog.Play(AudioBuses.Voice, $"{line.Theme}: {line.Text}");
        var words = line.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var volume = (int)Math.Round(100 * AudioBuses.BuiltInVoiceVolume);
        if (_builtInVoice is null || volume <= 0)
        {
            // Off (or muted): nothing to hear, but the line holds the voice as long as saying it would.
            _mode = Mode.Silent;
            _deadline = Now + 0.5 + (words * SecondsPerWord);
            return;
        }
        var hint = _hints.TryGetValue(line.Theme, out var h) ? h : new FallbackVoice();
        var rate = Math.Clamp(hint.Rate, 0.5f, 2f);
        _utterance++;
        _utteranceStarted = false;
        _mode = Mode.BuiltIn;
        _deadline = Now + 3 + (1.5 * words * SecondsPerWord / rate);
        DisplayServer.TtsSpeak(line.Text, _builtInVoice, volume, Math.Clamp(hint.Pitch, 0f, 2f), rate, _utterance, interrupt: true);
    }

    private void OnUtterance(long id, bool ended)
    {
        if (id != _utterance || _mode != Mode.BuiltIn)
        {
            return;
        }
        if (ended)
        {
            Done();
        }
        else
        {
            _utteranceStarted = true;
        }
    }

    /// <summary>Cuts off whatever is playing or being fetched.</summary>
    private void Cut()
    {
        _token++;
        _fetch?.Cancel();
        _fetch = null;
        switch (_mode)
        {
            case Mode.Clip:
                _player.Stop();
                break;
            case Mode.BuiltIn:
                _utterance++;   // ignore the cancelled utterance's callback
                DisplayServer.TtsStop();
                break;
        }
        _mode = Mode.Idle;
        _queue.Finish();
    }

    private void Done()
    {
        _mode = Mode.Idle;
        _utteranceStarted = false;
        _queue.Finish();
    }

    private void Report()
    {
        var state = State;
        if (state != _reported)
        {
            _reported = state;
            GD.Print($"NARRATOR {state}");
        }
    }

    /// <summary>The preferred voice if it matches, else the first English voice, else the first voice; null when there are none.</summary>
    private static string? PickBuiltInVoice(string? preferred, out string description)
    {
        description = "";
        if (OS.GetName() is "Linux" or "FreeBSD" && !HasSpeechDispatcher())
        {
            return null;   // asking Godot would only log "Parameter "synth" is null" errors
        }
        Godot.Collections.Array<Godot.Collections.Dictionary> voices;
        try
        {
            voices = DisplayServer.TtsGetVoices();
        }
        catch (Exception ex)
        {
            GD.Print($"NARRATOR built-in TTS unavailable: {ex.Message}");
            return null;
        }
        var list = voices
            .Select(v => (Id: v.TryGetValue("id", out var id) ? id.AsString() : "",
                          Name: v.TryGetValue("name", out var n) ? n.AsString() : "",
                          Language: v.TryGetValue("language", out var l) ? l.AsString() : ""))
            .Where(v => v.Id.Length > 0)
            .ToList();
        if (list.Count == 0)
        {
            return null;
        }
        var pick = list.FirstOrDefault(v => !string.IsNullOrWhiteSpace(preferred)
                                            && (v.Id.Contains(preferred, StringComparison.OrdinalIgnoreCase)
                                                || v.Name.Contains(preferred, StringComparison.OrdinalIgnoreCase)));
        if (pick.Id is null)
        {
            pick = list.FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        }
        if (pick.Id is null)
        {
            pick = list[0];
        }
        description = $"{pick.Name} ({pick.Language})";
        return pick.Id;
    }

    /// <summary>Godot's Linux TTS loads speech-dispatcher's client library at run time; without it there are no voices.</summary>
    private static bool HasSpeechDispatcher()
    {
        if (!System.Runtime.InteropServices.NativeLibrary.TryLoad("libspeechd.so.2", out var handle))
        {
            return false;
        }
        System.Runtime.InteropServices.NativeLibrary.Free(handle);
        return true;
    }
}
