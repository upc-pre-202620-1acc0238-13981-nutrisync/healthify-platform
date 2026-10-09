using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

public interface INutritionalAssessmentCommandService
{
    /// <summary>Subflow 3.1 - Record Assessment.</summary>
    Task<Result<NutritionalAssessment, NutritionalCareError>> Handle(RecordAssessmentCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 3.1 - Take Clinical Measurement.</summary>
    Task<Result<NutritionalAssessment, NutritionalCareError>> Handle(TakeClinicalMeasurementCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Subflow 3.1 - Close Assessment.</summary>
    Task<Result<NutritionalAssessment, NutritionalCareError>> Handle(CloseAssessmentCommand command,
        CancellationToken cancellationToken = default);
}
