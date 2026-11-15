using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SwirStreamAI;

// An ephemeral loopback-only HLS fixture for the self-test; never started during normal use.
sealed class TestMediaServer : IDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource cancel = new();
    readonly Task loop;
    readonly string folder;
    public string Url { get; }
    public TestMediaServer(string folder)
    {
        this.folder = folder; listener.Start();
        Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/index.m3u8";
        loop = Serve();
    }
    async Task Serve()
    {
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancel.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
                string request = await reader.ReadLineAsync(cancel.Token) ?? "";
                string? header;
                do { header = await reader.ReadLineAsync(cancel.Token); } while (!string.IsNullOrEmpty(header));
                string name = request.Split(' ').ElementAtOrDefault(1)?.TrimStart('/') ?? "";
                bool allowed = name == "index.m3u8" || System.Text.RegularExpressions.Regex.IsMatch(name, @"^segment\d+\.ts$");
                string path = Path.Combine(folder, allowed ? name : "not-found");
                bool exists = allowed && File.Exists(path);
                var bytes = exists ? await File.ReadAllBytesAsync(path, cancel.Token) : Array.Empty<byte>();
                string mime = name.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : "video/mp2t";
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {(exists ? "200 OK" : "404 Not Found")}\r\nContent-Type: {mime}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, cancel.Token); await stream.WriteAsync(bytes, cancel.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException or IOException) { }
    }
    public void Dispose() { cancel.Cancel(); listener.Stop(); try { loop.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } cancel.Dispose(); }
}
