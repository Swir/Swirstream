namespace SwirStreamAI;

public record DiscoveryBatch(string Name, List<Station> Stations, string Detail);
public record DiscoveryJob(string Name, Func<CancellationToken, Task<DiscoveryBatch>> Run);
public static class DiscoveryPipeline
{
    public static async Task Run(IEnumerable<DiscoveryJob> jobs, Action<DiscoveryBatch> publish, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(1);
        async Task Execute(DiscoveryJob job)
        {
            DiscoveryBatch batch;
            try { batch = await job.Run(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { batch = new(job.Name, [], "BŁĄD " + job.Name + ": " + ex.Message); }
            ct.ThrowIfCancellationRequested();
            await gate.WaitAsync(ct);
            try { ct.ThrowIfCancellationRequested(); publish(batch); } finally { gate.Release(); }
        }
        await Task.WhenAll(jobs.Select(Execute));
    }
}
