namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>
///     IA-0. Port to the language model provider. One structured call: the provider is asked to answer JSON that
///     follows <see cref="LanguageModelRequest.OutputSchemaJson" /> (native structured output). The answer is still
///     untrusted text: the pipeline validates it before anyone reads it.
/// </summary>
public interface ILanguageModelClient
{
    /// <exception cref="LanguageModelException">The provider did not produce an answer.</exception>
    Task<LanguageModelResponse> GenerateStructuredAsync(LanguageModelRequest request,
        CancellationToken cancellationToken = default);
}

/// <param name="SystemInstruction">The prompt: role, ethical rules, language. Never patient data.</param>
/// <param name="UserContent">The pseudonymized input, as JSON.</param>
/// <param name="OutputSchemaJson">JSON Schema of the expected output.</param>
/// <param name="MaxOutputTokens">Ceiling of the answer.</param>
/// <param name="Timeout">Bound of each attempt (<c>Ai:TimeoutSeconds</c>).</param>
/// <param name="Images">
///     IN-7. Images sent with the text (inline), already stripped of metadata by the caller. Null or empty for a
///     text-only request. Never logged.
/// </param>
public record LanguageModelRequest(
    string SystemInstruction,
    string UserContent,
    string OutputSchemaJson,
    int MaxOutputTokens,
    TimeSpan Timeout,
    IReadOnlyList<AiImagePart>? Images = null);

/// <param name="OutputText">The text the model answered; JSON when the provider honoured the schema.</param>
/// <param name="Model">The model id that answered, as the provider reports it.</param>
/// <param name="InputTokens">Tokens of the prompt and input, for cost.</param>
/// <param name="OutputTokens">Tokens of the answer (thinking included), for cost.</param>
public record LanguageModelResponse(string OutputText, string Model, int InputTokens, int OutputTokens);

/// <summary>Why the provider did not produce an answer.</summary>
public enum LanguageModelFailure
{
    /// <summary>No API key, project or credentials.</summary>
    NotConfigured,

    /// <summary>Every attempt exceeded the timeout.</summary>
    Timeout,

    /// <summary>5xx on every attempt.</summary>
    ServerError,

    /// <summary>4xx (including the provider's own 429): not retried.</summary>
    ClientError,

    /// <summary>The connection failed before any status.</summary>
    Network,

    /// <summary>An answer arrived but carries no usable text (blocked by safety, truncated, malformed).</summary>
    InvalidResponse
}

public sealed class LanguageModelException(LanguageModelFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public LanguageModelFailure Failure { get; } = failure;
}
