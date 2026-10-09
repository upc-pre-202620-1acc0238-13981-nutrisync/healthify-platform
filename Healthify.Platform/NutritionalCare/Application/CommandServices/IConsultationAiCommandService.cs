using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

/// <summary>
///     IA-6 and IA-7: the AI suggestions of the guided consultation, for the practitioner leading it. Each has a
///     deterministic fallback, so an AI that is off, failing, rejected or not consented to never empties the screen;
///     the errors are only those of the consultation itself.
/// </summary>
public interface IConsultationAiCommandService
{
    /// <summary>IA-6 - EV-3 "Sugerencia de IA", or the WHO category of the body mass index (source "Rule").</summary>
    Task<Result<DiagnosisSuggestion, NutritionalCareError>> Handle(SuggestConsultationDiagnosisCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>IA-7 - EV-5 guideline chips, or the fixed table of NC-6 (source "Rule").</summary>
    Task<Result<GuidelineSuggestions, NutritionalCareError>> Handle(SuggestConsultationGuidelinesCommand command,
        CancellationToken cancellationToken = default);
}
