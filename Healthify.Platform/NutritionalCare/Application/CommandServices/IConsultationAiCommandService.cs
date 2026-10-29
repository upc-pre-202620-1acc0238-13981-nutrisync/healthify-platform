using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

/// <summary>
///     The AI suggestions of the guided consultation, for the practitioner leading it. Each has a
///     deterministic fallback, so an AI that is off, failing, rejected or not consented to never empties the screen;
///     the errors are only those of the consultation itself.
/// </summary>
public interface IConsultationAiCommandService
{
    /// <summary>The AI diagnosis suggestion, or the WHO category of the body mass index (source "Rule").</summary>
    Task<Result<DiagnosisSuggestion, NutritionalCareError>> Handle(SuggestConsultationDiagnosisCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>The guideline chips of step 4, or the fixed default table (source "Rule").</summary>
    Task<Result<GuidelineSuggestions, NutritionalCareError>> Handle(SuggestConsultationGuidelinesCommand command,
        CancellationToken cancellationToken = default);
}
