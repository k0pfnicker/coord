using System.Net;
using System.Net.Http;
using Coord.Ai;
using Xunit;

namespace Coord.Tests;

public sealed class AiProviderTests
{
    [Fact]
    public async Task OpenAiResponsesAdapterUsesBearerAndParsesOutput()
    {
        var handler = new RecordingHandler("""{"output_text":"hello"}""");
        using var client = new HttpClient(handler);
        var provider = new OpenAiProvider(client, "secret", "test-model", "https://example.test/v1");

        Assert.Equal("hello", await provider.CompleteAsync("prompt"));
        Assert.Equal("/v1/responses", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer secret", handler.Request.Headers.Authorization!.ToString());
        Assert.DoesNotContain("secret", handler.Request.RequestUri.ToString());
    }

    [Fact]
    public async Task GeminiAdapterUsesApiKeyHeaderNotUrl()
    {
        var handler = new RecordingHandler("""{"candidates":[{"content":{"parts":[{"text":"answer"}]}}]}""");
        using var client = new HttpClient(handler);
        var provider = new GeminiProvider(client, "secret", "gemini-test", "https://example.test/v1beta");

        Assert.Equal("answer", await provider.CompleteAsync("prompt"));
        Assert.Equal("/v1beta/models/gemini-test:generateContent", handler.Request!.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("secret", handler.Request.RequestUri.ToString());
        Assert.Equal("secret", handler.Request.Headers.GetValues("x-goog-api-key").Single());
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response)
            });
        }
    }
}
