namespace AiCapex.Infrastructure.Transcripts;

public interface IWhisperTranscriptProvider
{
    Task<string?> TranscribeAsync(string audioUrlOrPath, CancellationToken cancellationToken = default);
}

public sealed class WhisperTranscriptProvider : IWhisperTranscriptProvider
{
    public Task<string?> TranscribeAsync(string audioUrlOrPath, CancellationToken cancellationToken = default)
    {
        // TODO: download earnings call audio from configured audio URLs.
        // TODO: transcribe with local Whisper/faster-whisper.
        // TODO: diarize speakers if possible.
        // TODO: store transcript with timestamps.
        return Task.FromResult<string?>(null);
    }
}
