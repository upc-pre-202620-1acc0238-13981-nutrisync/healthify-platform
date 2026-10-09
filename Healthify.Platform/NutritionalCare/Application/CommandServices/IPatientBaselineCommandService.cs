using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

public interface IPatientBaselineCommandService
{
    /// <summary>NC-1 - Record the patient baseline.</summary>
    Task<Result<PatientBaseline, NutritionalCareError>> Handle(RecordPatientBaselineCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>NC-1 - Edit the patient baseline.</summary>
    Task<Result<PatientBaseline, NutritionalCareError>> Handle(UpdatePatientBaselineCommand command,
        CancellationToken cancellationToken = default);
}
