using Healthify.Platform.CareRelationship.Application.QueryServices;
using Healthify.Platform.CareRelationship.Domain.Model.Queries;
using Healthify.Platform.CareRelationship.Domain.Repositories;

namespace Healthify.Platform.CareRelationship.Application.Internal.QueryServices;

public class AiPreferencesQueryService(
    IAiPreferencesRepository preferencesRepository,
    ICareLinkRepository careLinkRepository) : IAiPreferencesQueryService
{
    public async Task<AiPreferencesStatus> Handle(GetAiPreferencesByPatientIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var link = await careLinkRepository.FindActiveByPatientIdAsync(query.PatientId, cancellationToken);
        var preferences = await preferencesRepository.FindByPatientIdAsync(query.PatientId, cancellationToken);

        return AiPreferencesStatus.Of(query.PatientId, link is { HasAiProcessingConsent: true },
            preferences?.WeeklySummaryEnabled ?? false,
            preferences?.MealIdeasEnabled ?? false,
            preferences?.SuggestedQuestionsEnabled ?? false,
            preferences?.MealPhotoRecognitionEnabled ?? false);
    }
}
