namespace SwirStreamAI;

// Owns the native engine even if the window closes while native engine initialization is running.
public sealed class PlayerStartup : IDisposable
{
    readonly object gate = new();
    SwirPlayer? owned;
    bool disposed;
    public Task<SwirPlayer> Ready { get; }
    public PlayerStartup(Func<SwirPlayer> create)
    {
        Ready = Task.Run(() =>
        {
            var result = create();
            lock (gate)
            {
                if (!disposed) { owned = result; return result; }
            }
            result.Dispose();
            throw new OperationCanceledException("Okno zostało zamknięte.");
        });
    }
    public void Dispose()
    {
        SwirPlayer? release;
        lock (gate) { disposed = true; release = owned; owned = null; }
        release?.Dispose();
    }
}
