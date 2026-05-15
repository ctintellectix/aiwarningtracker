using System.Collections.Concurrent;
using System.Text.Json;

namespace AiCapex.Api.Services;

public interface IImportJobService
{
    ImportJobStatusDto Start(string kind, Func<IImportJobProgress, CancellationToken, Task<object>> work);
    ImportJobStatusDto? Get(Guid id);
}

public interface IImportJobProgress
{
    void Report(int progressPercent, string message);
}

public sealed record ImportJobStatusDto(
    Guid Id,
    string Kind,
    string Status,
    int ProgressPercent,
    string Message,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    JsonElement? Result);

public sealed class ImportJobService(ILogger<ImportJobService> logger) : IImportJobService
{
    private readonly ConcurrentDictionary<Guid, MutableImportJob> jobs = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ImportJobStatusDto Start(string kind, Func<IImportJobProgress, CancellationToken, Task<object>> work)
    {
        var job = new MutableImportJob(kind);
        jobs[job.Id] = job;
        _ = RunAsync(job, work);
        return job.Snapshot();
    }

    public ImportJobStatusDto? Get(Guid id) => jobs.TryGetValue(id, out var job) ? job.Snapshot() : null;

    private async Task RunAsync(MutableImportJob job, Func<IImportJobProgress, CancellationToken, Task<object>> work)
    {
        try
        {
            job.MarkRunning();
            var result = await work(job, CancellationToken.None);
            job.MarkCompleted(JsonSerializer.SerializeToElement(result, JsonOptions));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import job {JobId} failed.", job.Id);
            job.MarkFailed(ex.Message);
        }
    }

    private sealed class MutableImportJob(string kind) : IImportJobProgress
    {
        private readonly object gate = new();
        private string status = "Queued";
        private int progressPercent;
        private string message = "Queued.";
        private DateTimeOffset? completedAtUtc;
        private JsonElement? result;

        public Guid Id { get; } = Guid.NewGuid();
        public string Kind { get; } = kind;
        public DateTimeOffset CreatedAtUtc { get; } = DateTimeOffset.UtcNow;

        public void MarkRunning() => Report(1, "Starting import.");

        public void Report(int progressPercent, string message)
        {
            lock (gate)
            {
                status = "Running";
                this.progressPercent = Math.Clamp(progressPercent, 0, 99);
                this.message = message;
            }
        }

        public void MarkCompleted(JsonElement result)
        {
            lock (gate)
            {
                status = "Completed";
                progressPercent = 100;
                message = "Import completed.";
                completedAtUtc = DateTimeOffset.UtcNow;
                this.result = result;
            }
        }

        public void MarkFailed(string message)
        {
            lock (gate)
            {
                status = "Failed";
                this.message = message;
                completedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        public ImportJobStatusDto Snapshot()
        {
            lock (gate)
            {
                return new ImportJobStatusDto(Id, Kind, status, progressPercent, message, CreatedAtUtc, completedAtUtc, result);
            }
        }
    }
}
