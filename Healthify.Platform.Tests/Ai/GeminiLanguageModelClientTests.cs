using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Infrastructure.Ai.Gemini;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Healthify.Platform.Tests.Ai;

/// <summary>
///     IA-0. The Gemini client against a fake HTTP handler: what it sends (structured output, key or token, model),
///     what it reads, and its resilience (one retry, only on 5xx or timeout). Nothing reaches Google.
/// </summary>
public class GeminiLanguageModelClientTests
{
    private const string Answer = """
        {
          "candidates": [{
            "content": { "role": "model", "parts": [
              { "text": "thinking...", "thought": true },
              { "text": "{\"headline\":" }, { "text": "\"Bien\"}" } ] },
            "finishReason": "STOP" }],
          "usageMetadata": { "promptTokenCount": 321, "candidatesTokenCount": 45, "thoughtsTokenCount": 5 },
          "modelVersion": "gemini-3.5-flash"
        }
        """;

    private static readonly LanguageModelRequest Request = new("You are kind. Answer in es.",
        """{"daysLogged":6}""", """{"type":"object","properties":{"headline":{"type":"string"}}}""", 512,
        TimeSpan.FromMilliseconds(300));

    [Fact]
    public async Task Gemini_api_mode_sends_structured_output_with_the_key_header_to_the_default_model()
    {
        var handler = new StubHandler(_ => Ok(Answer));

        var response = await Client(handler, new() { ["Ai:Gemini:ApiKey"] = "test-key" })
            .GenerateStructuredAsync(Request);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:generateContent",
            sent.Uri.ToString());
        Assert.Equal("test-key", sent.Headers["x-goog-api-key"]);
        Assert.Null(sent.Authorization);
        var body = JsonNode.Parse(sent.Body)!;
        Assert.Equal("application/json", body["generationConfig"]!["responseMimeType"]!.GetValue<string>());
        Assert.Equal("object", body["generationConfig"]!["responseJsonSchema"]!["type"]!.GetValue<string>());
        Assert.Equal(512, body["generationConfig"]!["maxOutputTokens"]!.GetValue<int>());
        Assert.Equal(Request.SystemInstruction,
            body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(Request.UserContent, body["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>());

        Assert.Equal("""{"headline":"Bien"}""", response.OutputText); // thoughts skipped, parts joined
        Assert.Equal("gemini-3.5-flash", response.Model);
        Assert.Equal((321, 50), (response.InputTokens, response.OutputTokens));
    }

    [Fact]
    public async Task Vertex_mode_uses_the_project_location_and_an_adc_bearer_token()
    {
        var handler = new StubHandler(_ => Ok(Answer));
        var tokens = Substitute.For<IGoogleAccessTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("adc-token");

        await Client(handler, new()
        {
            ["Ai:Gemini:Mode"] = "VertexAi", ["Ai:Gemini:ProjectId"] = "nutrisense-prod",
            ["Ai:Gemini:Location"] = "us-central1", ["Ai:Model"] = "gemini-3.5-flash"
        }, tokens).GenerateStructuredAsync(Request);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://us-central1-aiplatform.googleapis.com/v1/projects/nutrisense-prod/locations/" +
                     "us-central1/publishers/google/models/gemini-3.5-flash:generateContent", sent.Uri.ToString());
        Assert.Equal("Bearer adc-token", sent.Authorization);
        Assert.False(sent.Headers.ContainsKey("x-goog-api-key"));
    }

    [Fact]
    public async Task Vertex_global_location_uses_the_global_host()
    {
        var handler = new StubHandler(_ => Ok(Answer));
        var tokens = Substitute.For<IGoogleAccessTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("t");

        await Client(handler, new() { ["Ai:Gemini:Mode"] = "VertexAi", ["Ai:Gemini:ProjectId"] = "p" }, tokens)
            .GenerateStructuredAsync(Request);

        Assert.StartsWith("https://aiplatform.googleapis.com/v1/projects/p/locations/global/",
            Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task A_5xx_is_retried_once_and_then_succeeds()
    {
        var handler = new StubHandler(attempt => attempt == 1 ? Status(HttpStatusCode.ServiceUnavailable) : Ok(Answer));

        var response = await Client(handler).GenerateStructuredAsync(Request);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("""{"headline":"Bien"}""", response.OutputText);
    }

    [Fact]
    public async Task Two_5xx_fail_as_server_error_after_exactly_one_retry()
    {
        var handler = new StubHandler(_ => Status(HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<LanguageModelException>(() => Client(handler).GenerateStructuredAsync(Request));

        Assert.Equal(LanguageModelFailure.ServerError, ex.Failure);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_timeout_is_retried_once()
    {
        var handler = new StubHandler(attempt => attempt == 1 ? null : Ok(Answer)); // null: never answers

        var response = await Client(handler).GenerateStructuredAsync(Request);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("gemini-3.5-flash", response.Model);
    }

    [Fact]
    public async Task Two_timeouts_fail_as_timeout()
    {
        var handler = new StubHandler(_ => null);

        var ex = await Assert.ThrowsAsync<LanguageModelException>(() => Client(handler).GenerateStructuredAsync(Request));

        Assert.Equal(LanguageModelFailure.Timeout, ex.Failure);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_4xx_is_not_retried(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => Status(status));

        var ex = await Assert.ThrowsAsync<LanguageModelException>(() => Client(handler).GenerateStructuredAsync(Request));

        Assert.Equal(LanguageModelFailure.ClientError, ex.Failure);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("""{"promptFeedback":{"blockReason":"SAFETY"}}""")]
    [InlineData("""{"candidates":[]}""")]
    [InlineData("""{"candidates":[{"content":{"parts":[{"text":"{}"}]},"finishReason":"MAX_TOKENS"}]}""")]
    [InlineData("""not json""")]
    public async Task An_answer_without_usable_text_is_an_invalid_response(string payload)
    {
        var ex = await Assert.ThrowsAsync<LanguageModelException>(() =>
            Client(new StubHandler(_ => Ok(payload))).GenerateStructuredAsync(Request));

        Assert.Equal(LanguageModelFailure.InvalidResponse, ex.Failure);
    }

    [Fact]
    public async Task Without_an_api_key_nothing_is_sent()
    {
        var handler = new StubHandler(_ => Ok(Answer));

        var ex = await Assert.ThrowsAsync<LanguageModelException>(() =>
            Client(handler, new() { ["Ai:Gemini:ApiKey"] = "" }).GenerateStructuredAsync(Request));

        Assert.Equal(LanguageModelFailure.NotConfigured, ex.Failure);
        Assert.Empty(handler.Requests);
    }

    private static GeminiLanguageModelClient Client(StubHandler handler, Dictionary<string, string?>? settings = null,
        IGoogleAccessTokenProvider? tokens = null)
    {
        var values = new Dictionary<string, string?> { ["Ai:Gemini:ApiKey"] = "test-key" };
        foreach (var (key, value) in settings ?? []) values[key] = value;
        return new GeminiLanguageModelClient(new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            tokens ?? Substitute.For<IGoogleAccessTokenProvider>(), NullLogger<GeminiLanguageModelClient>.Instance);
    }

    private static HttpResponseMessage Ok(string payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    }

    private static HttpResponseMessage Status(HttpStatusCode status)
    {
        return new HttpResponseMessage(status) { Content = new StringContent("{}") };
    }

    private sealed record SentRequest(Uri Uri, Dictionary<string, string> Headers, string? Authorization, string Body);

    /// <summary>Answers each attempt (1-based) with the response, or never answers when it is null.</summary>
    private sealed class StubHandler(Func<int, HttpResponseMessage?> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(request.RequestUri!,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                request.Headers.Authorization?.ToString(),
                await request.Content!.ReadAsStringAsync(cancellationToken)));

            var response = respond(Requests.Count);
            if (response is not null) return response;

            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
