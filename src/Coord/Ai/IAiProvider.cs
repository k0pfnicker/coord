using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Coord.Ai;

public interface IAiProvider
{
    string Name { get; }
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);
}

public abstract class HttpAiProvider : IAiProvider
{
    private readonly HttpClient client;
    private readonly string apiKey;
    private readonly TimeSpan timeout;

    protected HttpAiProvider(HttpClient client, string apiKey, TimeSpan? timeout = null)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new InvalidOperationException($"{Name} requires an API key.")
            : apiKey;
        timeout = timeout ?? TimeSpan.FromSeconds(60);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        this.timeout = timeout.Value;
    }

    public abstract string Name { get; }
    protected abstract HttpRequestMessage CreateRequest(string prompt);
    protected abstract string ReadCompletion(JsonElement document);

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("Prompt is required.", nameof(prompt));
        using var request = CreateRequest(prompt);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
        var body = await response.Content.ReadAsStringAsync(timeoutSource.Token);
        if (!response.IsSuccessStatusCode)
            throw new AiProviderException($"{Name} request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        try
        {
            using var document = JsonDocument.Parse(body);
            var completion = ReadCompletion(document.RootElement);
            if (string.IsNullOrWhiteSpace(completion)) throw new FormatException("The provider returned no completion.");
            return completion.Trim();
        }
        catch (JsonException ex) { throw new AiProviderException($"{Name} returned invalid JSON.", ex); }
        catch (FormatException ex) { throw new AiProviderException($"{Name} returned an unexpected response.", ex); }
    }

    protected HttpRequestMessage JsonRequest(HttpMethod method, string uri, object payload)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }
}

public sealed class AiProviderException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class OpenAiProvider(HttpClient client, string apiKey, string model, string baseUrl, TimeSpan? timeout = null)
    : HttpAiProvider(client, apiKey, timeout)
{
    public override string Name => "openai";
    protected override HttpRequestMessage CreateRequest(string prompt) =>
        JsonRequest(HttpMethod.Post, new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "responses").ToString(),
            new { model, input = prompt });
    protected override string ReadCompletion(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText)) return outputText.GetString() ?? "";
        if (!root.TryGetProperty("output", out var output)) return "";
        return string.Join("", output.EnumerateArray().SelectMany(item =>
            item.TryGetProperty("content", out var content)
                ? content.EnumerateArray().Where(c => c.TryGetProperty("text", out _))
                    .Select(c => c.GetProperty("text").GetString() ?? "")
                : []));
    }
}

public sealed class GeminiProvider : HttpAiProvider
{
    private readonly string apiKey;
    private readonly string model;
    private readonly string baseUrl;

    public GeminiProvider(HttpClient client, string apiKey, string model, string baseUrl, TimeSpan? timeout = null)
        : base(client, apiKey, timeout)
    {
        this.apiKey = apiKey;
        this.model = model;
        this.baseUrl = baseUrl;
    }

    public override string Name => "gemini";
    protected override HttpRequestMessage CreateRequest(string prompt)
    {
        var uri = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"),
            $"models/{Uri.EscapeDataString(model)}:generateContent");
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new { contents = new[] { new { parts = new[] { new { text = prompt } } } } }),
            Encoding.UTF8, "application/json");
        return request;
    }
    protected override string ReadCompletion(JsonElement root) =>
        root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
}

public sealed class XaiGrokProvider(HttpClient client, string apiKey, string model, string baseUrl, TimeSpan? timeout = null)
    : HttpAiProvider(client, apiKey, timeout)
{
    public override string Name => "xai";
    protected override HttpRequestMessage CreateRequest(string prompt) =>
        JsonRequest(HttpMethod.Post, new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "chat/completions").ToString(),
            new { model, messages = new[] { new { role = "user", content = prompt } } });
    protected override string ReadCompletion(JsonElement root) =>
        root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
}
