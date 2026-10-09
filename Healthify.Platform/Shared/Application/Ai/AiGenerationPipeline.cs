using System.Diagnostics;
using System.Text.Json;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>One generation, as the context that owns the function asks for it.</summary>
/// <param name="Feature">The function.</param>
/// <param name="SubjectPatientId">Whose data it processes. Audited; never sent to the provider.</param>
/// <param name="RequestedByUserId">Who asked; null when a job generates it.</param>
/// <param name="Language"><c>es</c> or <c>en</c>.</param>
/// <param name="Input">The aggregates the function needs. Pseudonymized before it leaves (guard 3).</param>
/// <param name="KnownIdentifiers">Names of the people involved, masked if they appear in a text of the input.</param>
/// <param name="Images">
///     IN-7. Images the function sends with its input (a photo of a dish), without metadata. They are not
///     pseudonymized (they carry no text of the platform); the audit keeps only their SHA-256.
/// </param>
public record AiGenerationRequest(
    AiFeature Feature,
    int SubjectPatientId,
    int? RequestedByUserId,
    string Language,
    object Input,
    IReadOnlyCollection<string>? KnownIdentifiers = null,
    IReadOnlyList<AiImagePart>? Images = null);

/// <summary>A validated output and its trace.</summary>
/// <param name="Output">The output, schema-valid and accepted by the validator of the function.</param>
/// <param name="GenerationId">The <c>ai_generations</c> row (<c>AiGenerationId</c> wherever the output is kept).</param>
/// <param name="PromptVersion">The prompt that produced it, for instance <c>meal-ideas@3</c>.</param>
/// <param name="Model">The model id that answered.</param>
public record AiGenerationOutcome<T>(T Output, long GenerationId, string PromptVersion, string Model);

/// <summary>Runs every AI generation of the platform through the IA-0 guards.</summary>
public interface IAiGenerationPipeline
{
    Task<Result<AiGenerationOutcome<T>, AiError>> GenerateAsync<T>(AiGenerationRequest request,
        IAiOutputValidator<T> validator, CancellationToken cancellationToken = default);
}

/// <summary>
///     IA-0. The guards every generation passes, in this order (the order is the specification):
///     1. kill switch and feature flag → <see cref="AiError.AiFeatureDisabled" />;
///     2. consent (<see cref="IAiConsentPolicy" />) → <see cref="AiError.AiConsentRequired" />;
///     3. minimization and pseudonymization (<see cref="AiInputPseudonymizer" />);
///     4. rate limit per function → <see cref="AiError.AiRateLimited" />;
///     5. the call, bounded by the timeout (the client retries once on 5xx or timeout) →
///     <see cref="AiError.AiProviderUnavailable" />;
///     6. output validation: JSON, schema, then the business rules of the function →
///     <see cref="AiError.AiOutputRejected" />;
///     7. audit: one <c>ai_generations</c> row for every generation that got as far as pseudonymization, also
///     when a later guard stops it or the provider fails.
/// </summary>
/// <remarks>
///     Guards 1 and 2 stop the generation before any patient data is processed: they leave no row, only an
///     Information log with the feature and the reason (never the patient). From guard 3 on, everything is audited.
///     No business rule lives here: what a function may say is its context's validator, and who may ask is its
///     context's command service. This class knows no context.
/// </remarks>
public class AiGenerationPipeline(
    IAiSettings settings,
    IAiConsentPolicy consentPolicy,
    IPromptCatalog promptCatalog,
    ILanguageModelClient languageModelClient,
    IAiGenerationLog generationLog,
    TimeProvider timeProvider,
    ILogger<AiGenerationPipeline> logger) : IAiGenerationPipeline
{
    private static readonly JsonSerializerOptions OutputOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<AiGenerationOutcome<T>, AiError>> GenerateAsync<T>(AiGenerationRequest request,
        IAiOutputValidator<T> validator, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(validator);

        var trace = new Trace(request, timeProvider.GetUtcNow(), Stopwatch.StartNew());

        try
        {
            // Guard 1: kill switch and feature flag.
            if (!settings.IsEnabled(request.Feature))
                return NotAttempted<T>(request, AiError.AiFeatureDisabled);

            // Guard 2: consent. The policy degrades to false, but an exception here still means "no".
            if (!await IsAllowedAsync(request, cancellationToken))
                return NotAttempted<T>(request, AiError.AiConsentRequired);

            // Guard 3: minimization and pseudonymization. Only this leaves the platform. From here on, the
            // patient's data is being processed, so every outcome is audited.
            trace.Processing = true;
            var input = AiInputPseudonymizer.Pseudonymize(request.Input, request.KnownIdentifiers);
            trace.InputHash = input.Hash;
            // IN-7: an image is audited by its digest alone, never by its bytes.
            var images = request.Images is { Count: > 0 } parts ? parts : null;
            trace.InputImageHash = AiImagePart.CombinedHash(images);

            // Guard 4: rate limit, over the generations that reached the provider in the last 24 hours. A patient
            // function is counted per patient; a practitioner function, per practitioner.
            var requester = request.Feature.Audience == AiFeatureAudience.Practitioner
                ? request.RequestedByUserId
                : null;
            var used = await generationLog.CountSinceAsync(request.Feature, request.SubjectPatientId, requester,
                trace.StartedAt.AddDays(-1), cancellationToken);
            if (used >= settings.DailyLimit(request.Feature))
                return await Stop<T>(trace, AiGenerationStatus.Blocked, AiError.AiRateLimited);

            var prompt = promptCatalog.FindLatest(request.Feature);
            if (prompt is null)
            {
                logger.LogWarning("AI feature {Feature} has no prompt in the catalog; treated as disabled",
                    request.Feature.Name);
                return await Stop<T>(trace, AiGenerationStatus.Blocked, AiError.AiFeatureDisabled);
            }

            trace.PromptVersion = prompt.Version;

            // Guard 5: the call, bounded by the timeout. The client owns the one retry.
            LanguageModelResponse response;
            try
            {
                response = await languageModelClient.GenerateStructuredAsync(new LanguageModelRequest(
                    prompt.Render(request.Language), input.Json, prompt.OutputSchemaJson,
                    settings.MaxOutputTokens(request.Feature), settings.Timeout, images), cancellationToken);
            }
            catch (LanguageModelException ex) when (ex.Failure == LanguageModelFailure.InvalidResponse)
            {
                logger.LogWarning("AI feature {Feature}: the provider answer had no usable output ({Message})",
                    request.Feature.Name, ex.Message);
                return await Stop<T>(trace, AiGenerationStatus.Rejected, AiError.AiOutputRejected,
                    ex.Failure.ToString());
            }
            catch (LanguageModelException ex)
            {
                logger.LogWarning("AI feature {Feature}: the provider is unavailable ({Failure})",
                    request.Feature.Name, ex.Failure);
                return await Stop<T>(trace, AiGenerationStatus.Failed, AiError.AiProviderUnavailable,
                    ex.Failure.ToString());
            }

            trace.Model = response.Model;
            trace.InputTokens = response.InputTokens;
            trace.OutputTokens = response.OutputTokens;

            // Guard 6: JSON, schema, business rules of the function.
            var violations = new List<string>();
            var output = Parse<T>(response.OutputText, prompt.OutputSchemaJson, violations, out var outputJson);
            if (output is not null) violations.AddRange(validator.Validate(output));
            if (violations.Count > 0)
            {
                logger.LogWarning("AI feature {Feature}: output rejected by {Count} rule(s): {Violations}",
                    request.Feature.Name, violations.Count, string.Join(" | ", violations.Take(5)));
                return await Stop<T>(trace, AiGenerationStatus.Rejected, AiError.AiOutputRejected,
                    "OutputRejected");
            }

            // Guard 7: audit. A success that cannot be audited is not returned: its output would be untraceable.
            var id = await generationLog.RecordAsync(trace.ToRecord(AiGenerationStatus.Succeeded, null, outputJson,
                settings.Retention), cancellationToken);
            return new Result<AiGenerationOutcome<T>, AiError>.Success(
                new AiGenerationOutcome<T>(output!, id, prompt.Version, response.Model));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (trace.Processing)
                await TryRecordAsync(trace.ToRecord(AiGenerationStatus.Failed, "Cancelled", null, settings.Retention));
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI feature {Feature}: unexpected error in the generation pipeline",
                request.Feature.Name);
            if (trace.Processing)
                await TryRecordAsync(trace.ToRecord(AiGenerationStatus.Failed, nameof(AiError.UnexpectedError), null,
                    settings.Retention));
            return new Result<AiGenerationOutcome<T>, AiError>.Failure(AiError.UnexpectedError);
        }
    }

    private async Task<bool> IsAllowedAsync(AiGenerationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await consentPolicy.IsAllowedAsync(request.SubjectPatientId, request.Feature, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "AI consent lookup failed for feature {Feature}; treated as not allowed",
                request.Feature.Name);
            return false;
        }
    }

    private static T? Parse<T>(string text, string schemaJson, List<string> violations, out string? outputJson)
    {
        outputJson = null;
        try
        {
            using var document = JsonDocument.Parse(text);
            using var schema = JsonDocument.Parse(schemaJson);
            var schemaErrors = AiJsonSchemaValidator.Validate(document.RootElement, schema.RootElement);
            if (schemaErrors.Count > 0)
            {
                violations.AddRange(schemaErrors);
                return default;
            }

            var output = document.RootElement.Deserialize<T>(OutputOptions);
            if (output is null)
            {
                violations.Add("$: the output is null.");
                return default;
            }

            outputJson = document.RootElement.GetRawText();
            return output;
        }
        catch (JsonException ex)
        {
            violations.Add($"$: not valid JSON for the expected output ({ex.Message}).");
            return default;
        }
    }

    /// <summary>
    ///     Guards 1 and 2: nothing about the patient was processed, so nothing is audited. The log carries the
    ///     feature and the reason only, never the patient.
    /// </summary>
    private Result<AiGenerationOutcome<T>, AiError> NotAttempted<T>(AiGenerationRequest request, AiError reason)
    {
        logger.LogInformation("AI generation of {Feature} not attempted: {Reason}", request.Feature.Name, reason);
        return new Result<AiGenerationOutcome<T>, AiError>.Failure(reason);
    }

    private async Task<Result<AiGenerationOutcome<T>, AiError>> Stop<T>(Trace trace, AiGenerationStatus status,
        AiError error, string? errorCode = null)
    {
        await TryRecordAsync(trace.ToRecord(status, errorCode ?? error.ToString(), null, settings.Retention));
        return new Result<AiGenerationOutcome<T>, AiError>.Failure(error);
    }

    /// <summary>The audit of a failure never hides the failure itself: if it cannot be written, it is logged.</summary>
    private async Task TryRecordAsync(AiGenerationRecord record)
    {
        try
        {
            // CancellationToken.None: an aborted request is still audited.
            await generationLog.RecordAsync(record, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not audit an AI generation of {Feature} with status {Status}",
                record.Feature, record.Status);
        }
    }

    /// <summary>What the audit row will say, filled in as the guards pass.</summary>
    private sealed class Trace(AiGenerationRequest request, DateTimeOffset startedAt, Stopwatch stopwatch)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;

        /// <summary>Set once pseudonymization starts: from then on, the generation is audited.</summary>
        public bool Processing { get; set; }
        public string? InputHash { get; set; }
        public string? InputImageHash { get; set; }
        public string? PromptVersion { get; set; }
        public string? Model { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }

        public AiGenerationRecord ToRecord(AiGenerationStatus status, string? errorCode, string? outputJson,
            TimeSpan retention)
        {
            return new AiGenerationRecord(request.Feature.Name, request.SubjectPatientId, request.RequestedByUserId,
                PromptVersion, Model, InputHash, outputJson, status, errorCode, InputTokens, OutputTokens,
                (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds), StartedAt, StartedAt + retention,
                InputImageHash);
        }
    }
}
