using Coord.Config;
using Coord.Games;
using Coord.Search;

namespace Coord.Ai;

public static class ProviderFactory
{
    public static IAiProvider CreateAiProvider(AiConfig config) => CreateAi(config);
    public static ISearchProvider CreateSearchProvider(AiConfig config) => CreateSearch(config);
    public static IIdentityProvider CreateIdentityProvider(AiConfig config) => CreateIdentity(config);

    public static IAiProvider CreateAi(CoordConfig config) =>
        CreateAi(config.Ai);

    public static ISearchProvider CreateSearch(CoordConfig config) =>
        CreateSearch(config.Ai);

    public static IIdentityProvider CreateIdentity(CoordConfig config) =>
        CreateIdentity(config.Ai);

    public static IAiProvider CreateAi(AiConfig config)
    {
        Validate(config);
        var provider = config.Provider.Trim().ToLowerInvariant();
        if (provider is "none" or "manual") return new ManualAiProvider();
        if (provider is not ("openai" or "gemini" or "xai" or "grok" or "copilot"))
            throw new InvalidOperationException($"Unknown AI provider '{config.Provider}'.");
        if (provider == "copilot")
            throw new InvalidOperationException(
                "Copilot is not supported as an API provider: this application has no official SDK/authentication runtime.");
        var key = ApiKey(provider);
        var client = new HttpClient();
        return provider switch
        {
            "openai" => new OpenAiProvider(client, key, config.Model ?? "gpt-4o-mini",
                config.BaseUrl ?? "https://api.openai.com/v1", config.Timeout),
            "gemini" => new GeminiProvider(client, key, config.Model ?? "gemini-2.0-flash",
                config.BaseUrl ?? "https://generativelanguage.googleapis.com/v1beta", config.Timeout),
            "xai" or "grok" => new XaiGrokProvider(client, key, config.Model ?? "grok-3-mini",
                config.BaseUrl ?? "https://api.x.ai/v1", config.Timeout),
            _ => throw new InvalidOperationException($"Unknown AI provider '{config.Provider}'.")
        };
    }

    public static class ResearchProviderFactory
    {
        public static IAiProvider CreateAi(AiConfig config) => ProviderFactory.CreateAi(config);
        public static ISearchProvider CreateSearch(AiConfig config) => ProviderFactory.CreateSearch(config);
        public static IIdentityProvider CreateIdentity(AiConfig config) => ProviderFactory.CreateIdentity(config);
    }

    public static ISearchProvider CreateSearch(AiConfig config)
    {
        Validate(config);
        return config.Provider.Trim().ToLowerInvariant() switch
        {
            "none" or "manual" => new ManualSearchProvider(),
            _ => throw new InvalidOperationException($"Search provider '{config.Provider}' is not available offline.")
        };
    }

    public static IIdentityProvider CreateIdentity(AiConfig config)
    {
        var ai = CreateAi(config);
        return ai is ManualAiProvider
            ? new ManualIdentityProvider(new Identity("Ada Lovelace", ["Ada"]))
            : new AiIdentityProvider(ai);
    }

    private static void Validate(AiConfig config) =>
        ArgumentNullException.ThrowIfNull(config);

    private static string ApiKey(string provider)
    {
        var names = provider switch
        {
            "openai" => new[] { "OPENAI_API_KEY", "COORD_AI_OPENAI_API_KEY", "Coord__Ai__OpenAiApiKey", "Ai__OpenAiApiKey" },
            "gemini" => new[] { "GEMINI_API_KEY", "COORD_AI_GEMINI_API_KEY", "Coord__Ai__GeminiApiKey", "Ai__GeminiApiKey" },
            "xai" or "grok" => new[] { "XAI_API_KEY", "COORD_AI_XAI_API_KEY", "Coord__Ai__XaiApiKey", "Ai__XaiApiKey" },
            _ => []
        };
        var key = names.Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return key ?? throw new InvalidOperationException(
            $"{provider} requires an API key. Set {names[0]} (or the documented .NET user-secrets-compatible setting).");
    }
}

public sealed class ManualAiProvider : IAiProvider
{
    public string Name => "manual";
    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default) =>
        Task.FromResult("Manual/offline provider: no AI completion was requested.");
}

public sealed class AiIdentityProvider(IAiProvider ai) : IIdentityProvider
{
    public async Task<Identity> ChooseAsync(string category, CancellationToken cancellationToken = default)
    {
        var prompt = $"""
            Choose one well-known person, fictional character, place, or thing in category "{category}".
            Return only valid JSON with string property "name" and string array property "aliases".
            Do not include markdown or additional properties.
            """;
        var completion = await ai.CompleteAsync(prompt, cancellationToken);
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(completion);
            var root = document.RootElement;
            var name = root.GetProperty("name").GetString();
            var aliases = root.TryGetProperty("aliases", out var values)
                ? values.EnumerateArray().Select(v => v.GetString() ?? "").Where(v => v.Length > 0).ToArray()
                : [];
            if (string.IsNullOrWhiteSpace(name)) throw new FormatException();
            return new Identity(name, aliases);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or FormatException)
        {
            throw new AiProviderException("AI identity selection returned invalid JSON.", ex);
        }
    }
}

public sealed class ManualSearchProvider : ISearchProvider
{
    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchResult>>(
            [new SearchResult("manual", $"Manual result for {query.Trim()}", 1)]);
}
