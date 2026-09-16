using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Coord.Protocol;

public interface ILineConnection : IAsyncDisposable
{
    Task SendAsync(IProtocolMessage message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<IProtocolMessage> ReadAllAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(string reason = "closed", CancellationToken cancellationToken = default);
}

public sealed class JsonLineConnection(Stream stream) : ILineConnection
{
    private readonly StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
    private readonly StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public async Task SendAsync(IProtocolMessage message, CancellationToken cancellationToken = default)
    {
        await writeLock.WaitAsync(cancellationToken);
        try { await writer.WriteLineAsync(ProtocolCodec.Serialize(message).AsMemory(), cancellationToken); }
        finally { writeLock.Release(); }
    }

    public async IAsyncEnumerable<IProtocolMessage> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) yield break;
            if (line.Length != 0) yield return ProtocolCodec.Deserialize(line);
        }
    }

    public async Task CloseAsync(string reason = "closed", CancellationToken cancellationToken = default)
    {
        try { await SendAsync(new DisconnectMessage(reason), cancellationToken); } catch { /* peer may be gone */ }
        await DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        writeLock.Dispose();
        reader.Dispose();
        writer.Dispose();
        stream.Dispose();
        return ValueTask.CompletedTask;
    }
}

public static class TcpLineConnection
{
    public static async Task<JsonLineConnection> AcceptAsync(
        TcpClient client, X509Certificate2? certificate, CancellationToken cancellationToken = default)
    {
        Stream stream = client.GetStream();
        if (certificate is not null)
        {
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                    System.Security.Authentication.SslProtocols.Tls13
            }, cancellationToken);
            stream = ssl;
        }
        return new JsonLineConnection(stream);
    }

    public static async Task<JsonLineConnection> ConnectAsync(
        string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default,
        bool useTls = false)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var client = new TcpClient();
        await client.ConnectAsync(host, port, timeoutCts.Token);
        Stream stream = client.GetStream();
        if (useTls)
        {
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                    System.Security.Authentication.SslProtocols.Tls13
            }, timeoutCts.Token);
            stream = ssl;
        }
        return new JsonLineConnection(stream);
    }

}
