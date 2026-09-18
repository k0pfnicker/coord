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

public sealed record AiConfig(
    string Provider,
    string? Model = null,
    string? BaseUrl = null,
    TimeSpan? Timeout = null);

public static class ConfigValidator
{
    public static void Validate(CoordConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Network.Address))
            throw new InvalidOperationException("network.address is required.");
        ConfigLoader.ParseAddress(config.Network.Address);
        if (config.Network.ConnectTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("network.connectTimeout must be positive.");
        if (string.IsNullOrWhiteSpace(config.Storage.DataDirectory))
            throw new InvalidOperationException("storage.dataDirectory is required.");
        if (string.IsNullOrWhiteSpace(config.Ai.Provider))
            throw new InvalidOperationException("ai.provider is required.");
        if (config.Ai.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new InvalidOperationException("ai.timeout must be positive.");
    }
}

public static class ConfigLoader
{
    public static CoordConfig Load(string? path)
    {
        var config = path is null || !File.Exists(path)
            ? CoordConfig.Default()
            : JsonSerializer.Deserialize<CoordConfig>(File.ReadAllText(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? CoordConfig.Default();
        config = ApplyEnvironment(config);
        ConfigValidator.Validate(config);
        return config;
    }

    private static CoordConfig ApplyEnvironment(CoordConfig config)
    {
        static string? Env(params string[] names) =>
            names.Select(Environment.GetEnvironmentVariable).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        var ai = config.Ai;
        return config with
        {
            Ai = ai with
            {
                Provider = Env("COORD_AI_PROVIDER", "Coord__Ai__Provider", "Ai__Provider") ?? ai.Provider,
                Model = Env("COORD_AI_MODEL", "Coord__Ai__Model", "Ai__Model") ?? ai.Model,
                BaseUrl = Env("COORD_AI_BASE_URL", "Coord__Ai__BaseUrl", "Ai__BaseUrl") ?? ai.BaseUrl,
                Timeout = int.TryParse(Env("COORD_AI_TIMEOUT_SECONDS", "Coord__Ai__TimeoutSeconds", "Ai__TimeoutSeconds"), out var seconds)
                    ? TimeSpan.FromSeconds(seconds)
                    : ai.Timeout
            }
        };
    }

    public static (string Host, int Port) ParseAddress(string address)
    {
        var parts = address.Split(':', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
            !int.TryParse(parts[1], out var port) || port is < 1 or > 65535)
            throw new FormatException($"Network address must be host:port, got '{address}'.");
        return (parts[0], port);
    }
}
