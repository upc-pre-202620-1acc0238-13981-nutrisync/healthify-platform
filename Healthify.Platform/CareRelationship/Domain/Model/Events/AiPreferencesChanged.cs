using Healthify.Platform.Shared.Domain.Model.Events;

namespace Healthify.Platform.CareRelationship.Domain.Model.Events;

/// <summary>
///     The patient's AI preferences changed, by their own choice or following the consent switch. Carries the
///     state after the change. Published only when something changed.
/// </summary>
/// <remarks>
///     Crosses the boundary: the context of a function that went off purges the content it generated for the
///     patient (MonitoringAdherence for the weekly summary and suggested questions, IntakeBodyResponse for meal
///     ideas and the temporary meal photo analyses), and this context purges its rows of the technical
///     audit. <c>MealPhotoRecognitionEnabled</c> is last so older producers stay compatible; false when they
///     do not say.
/// </remarks>
public record AiPreferencesChanged(
    int PatientId,
    bool WeeklySummaryEnabled,
    bool MealIdeasEnabled,
    bool SuggestedQuestionsEnabled,
    DateTimeOffset At,
    bool MealPhotoRecognitionEnabled = false) : DomainEventBase;
