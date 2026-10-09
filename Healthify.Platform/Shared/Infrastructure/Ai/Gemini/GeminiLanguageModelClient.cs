using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Infrastructure.Ai.Configuration;

namespace Healthify.Platform.Shared.Infrastructure.Ai.Gemini;

/// <summary>
///     IA-0. <see cref="ILanguageModelClient" /> over Gemini <c>generateContent</c>, with native structured output
///     (<c>responseMimeType: application/json</c> + <c>responseJsonSchema</c>).
/// </summary>
/// <remarks>
///     Two modes (<c>Ai:Gemini:Mode</c>):
///     - <c>GeminiApi</c> (default): <c>generativelanguage.googleapis.com</c> with <c>Ai:Gemini:ApiKey</c> in the
///     <c>x-goog-api-key</c> header. Never the free tier with real patient data (docs, §12-#5/#6).
///     - <c>VertexAi</c>: <c>{location}-aiplatform.googleapis.com</c> (or <c>aiplatform.googleapis.com</c> for
///     <c>global</c>) with <c>Ai:Gemini:ProjectId</c>, <c>Ai:Gemini:Location</c> and Application Default Credentials.
///     Resilience: each attempt is bounded by the request timeout (<c>Ai:TimeoutSeconds</c>), and there is exactly
///     one retry, only after a 5xx or a timeout. A 4xx (the provider's own 429 included) or a network failure is
///     not retried. Neither the key nor the input is ever logged.
///     IN-7: the images of the request travel as <c>inlineData</c> parts (base64) after the text, in the same user
///     turn. They exist only in the request body built here, which is never logged nor kept.
/// </remarks>
public class GeminiLanguageModelClient(
    HttpClient httpClient,
    IConfiguration configuration,
    IGoogleAccessTokenProvider tokenProvider,
    ILogger<GeminiLanguageModelClient> logger) : ILanguageModelClient
{
    public const string GeminiApiMode = "GeminiApi";
    public const string VertexAiMode = "VertexAi";
    public const string DefaultLocation = "global";
    private const int MaxAttempts = 2;

    public async Task<LanguageModelResponse> GenerateStructuredAsync(LanguageModelRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = configuration["Ai:Model"] is { Length: > 0 } configured
            ? configured
            : ConfiguredAiSettings.DefaultModel;
        var endpoint = await ResolveEndpointAsync(model, cancellationToken);
        var body = BuildBody(request);

        for (var attempt = 1;; attempt++)
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(request.Timeout);

            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint.Uri);
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");
            endpoint.Authorize(message);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(message, attemptTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    logger.LogInformation("Gemini call timed out after {Timeout}; retrying once", request.Timeout);
                    continue;
                }

                throw new LanguageModelException(LanguageModelFailure.Timeout,
                    $"Gemini did not answer within {request.Timeout} on {MaxAttempts} attempts.");
            }
            catch (HttpRequestException ex)
            {
                throw new LanguageModelException(LanguageModelFailure.Network, "Gemini could not be reached.", ex);
            }

            using (response)
            {
                if ((int)response.StatusCode >= 500)
                {
                    if (attempt < MaxAttempts)
                    {
                        logger.LogInformation("Gemini answered {Status}; retrying once", (int)response.StatusCode);
                        continue;
                    }

                    throw new LanguageModelException(LanguageModelFailure.ServerError,
                        $"Gemini answered {(int)response.StatusCode} on {MaxAttempts} attempts.");
                }

                if (!response.IsSuccessStatusCode)
                    throw new LanguageModelException(LanguageModelFailure.ClientError,
                        $"Gemini answered {(int)response.StatusCode} {Reason(response.StatusCode)}.");

                string payload;
                try
                {
                    payload = await response.Content.ReadAsStringAsync(attemptTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new LanguageModelException(LanguageModelFailure.Timeout,
                        "Gemini did not finish its answer in time.");
                }

                return ParseResponse(payload, model);
            }
        }
    }

    private async Task<Endpoint> ResolveEndpointAsync(string model, CancellationToken cancellationToken)
    {
        var mode = configuration["Ai:Gemini:Mode"] is { Length: > 0 } m ? m : GeminiApiMode;

        if (string.Equals(mode, VertexAiMode, StringComparison.OrdinalIgnoreCase))
        {
            var project = configuration["Ai:Gemini:ProjectId"];
            if (string.IsNullOrWhiteSpace(project))
                throw new LanguageModelException(LanguageModelFailure.NotConfigured,
                    "Ai:Gemini:ProjectId is required in VertexAi mode.");
            var location = configuration["Ai:Gemini:Location"] is { Length: > 0 } l ? l : DefaultLocation;
            var host = string.Equals(location, DefaultLocation, StringComparison.OrdinalIgnoreCase)
                ? "aiplatform.googleapis.com"
                : $"{location}-aiplatform.googleapis.com";

            string token;
            try
            {
                token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new LanguageModelException(LanguageModelFailure.NotConfigured,
                    "No Application Default Credentials for Vertex AI.", ex);
            }

            return new Endpoint(
                new Uri($"https://{host}/v1/projects/{Uri.EscapeDataString(project)}/locations/" +
                        $"{Uri.EscapeDataString(location)}/publishers/google/models/{Uri.EscapeDataString(model)}" +
                        ":generateContent"),
                message => message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token));
        }

        if (!string.Equals(mode, GeminiApiMode, StringComparison.OrdinalIgnoreCase))
            throw new LanguageModelException(LanguageModelFailure.NotConfigured,
                $"Ai:Gemini:Mode must be {GeminiApiMode} or {VertexAiMode}.");

        var apiKey = configuration["Ai:Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new LanguageModelException(LanguageModelFailure.NotConfigured,
                "Ai:Gemini:ApiKey is required in GeminiApi mode.");

        return new Endpoint(
            new Uri($"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}" +
                    ":generateContent"),
            message => message.Headers.Add("x-goog-api-key", apiKey));
    }

    private static string BuildBody(LanguageModelRequest request)
    {
        JsonNode schema;
        try
        {
            schema = JsonNode.Parse(request.OutputSchemaJson)
                     ?? throw new ArgumentException("The output schema is empty.", nameof(request));
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("The output schema is not valid JSON.", nameof(request), ex);
        }

        var parts = new JsonArray(new JsonObject { ["text"] = request.UserContent });
        foreach (var image in request.Images ?? [])
            parts.Add(new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = image.MimeType,
                    ["data"] = Convert.ToBase64String(image.Bytes.Span)
                }
            });

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.SystemInstruction })
            },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = parts
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseJsonSchema"] = schema,
                ["maxOutputTokens"] = request.MaxOutputTokens
            }
        };
        return body.ToJsonString();
    }

    private static LanguageModelResponse ParseResponse(string payload, string requestedModel)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("promptFeedback", out var feedback) &&
                feedback.TryGetProperty("blockReason", out var blockReason))
                throw new LanguageModelException(LanguageModelFailure.InvalidResponse,
                    $"Gemini blocked the prompt ({blockReason.GetString()}).");

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
                throw new LanguageModelException(LanguageModelFailure.InvalidResponse, "Gemini returned no candidate.");

            var candidate = candidates[0];
            var finishReason = candidate.TryGetProperty("finishReason", out var finish) ? finish.GetString() : null;
            if (finishReason is not null and not "STOP")
                throw new LanguageModelException(LanguageModelFailure.InvalidResponse,
                    $"Gemini stopped with {finishReason}.");

            var text = new StringBuilder();
            if (candidate.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
                foreach (var part in parts.EnumerateArray())
                {
                    // Thought summaries are not the answer.
                    if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                        continue;
                    if (part.TryGetProperty("text", out var partText)) text.Append(partText.GetString());
                }

            if (text.Length == 0)
                throw new LanguageModelException(LanguageModelFailure.InvalidResponse, "Gemini returned no text.");

            var inputTokens = 0;
            var outputTokens = 0;
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                inputTokens = Int(usage, "promptTokenCount");
                outputTokens = Int(usage, "candidatesTokenCount") + Int(usage, "thoughtsTokenCount");
            }

            var model = root.TryGetProperty("modelVersion", out var version) && version.GetString() is { Length: > 0 } v
                ? v
                : requestedModel;
            return new LanguageModelResponse(text.ToString(), model, inputTokens, outputTokens);
        }
        catch (JsonException ex)
        {
            throw new LanguageModelException(LanguageModelFailure.InvalidResponse, "Gemini returned malformed JSON.",
                ex);
        }
    }

    private static int Int(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;
    }

    private static string Reason(HttpStatusCode status)
    {
        return status == HttpStatusCode.TooManyRequests ? "(provider quota)" : string.Empty;
    }

    private sealed record Endpoint(Uri Uri, Action<HttpRequestMessage> Authorize);
}
