namespace ArcBox.Server.Speech;

/// <summary>Turns text into MP3 bytes with a provider. One attempt per call: no retries (the TV falls back instead).</summary>
public interface ISpeechSynthesizer
{
    /// <exception cref="SpeechSynthesisException">The provider failed, refused or timed out.</exception>
    Task<byte[]> SynthesizeAsync(string text, ResolvedVoice voice, CancellationToken ct);
}

/// <summary>A provider failure. The message is safe to log: it never contains the API key.</summary>
public sealed class SpeechSynthesisException(string message) : Exception(message);
