using System.Net;
using System.Text.Json;

namespace Coord.Config;

public sealed record CoordConfig(
    NetworkConfig Network,
    StorageConfig Storage,
    AiConfig Ai)
{
    public static CoordConfig Default() => new(
        new NetworkConfig("127.0.0.1:4242", TimeSpan.FromSeconds(10)),
        new StorageConfig("data"),
        new AiConfig("none"));
}

public sealed record NetworkConfig(
    string Address,
    TimeSpan ConnectTimeout,
    bool UseTls = false,
    string? ServerCertificatePath = null,
    string? ServerCertificatePassword = null);

public sealed record StorageConfig(string DataDirectory);

public sealed record AiConfig(string Provider);

public static class ConfigLoader
{
    public static CoordConfig Load(string? path)
    {
        if (path is null || !File.Exists(path)) return CoordConfig.Default();
        return JsonSerializer.Deserialize<CoordConfig>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? CoordConfig.Default();
    }

    public static (string Host, int Port) ParseAddress(string address)
    {
        var parts = address.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            throw new FormatException($"Network address must be host:port, got '{address}'.");
        return (parts[0], port);
    }
}
