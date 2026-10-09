using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;

namespace Healthify.Platform.NutritionalCare.Application.CommandServices;

public interface INutritionalDiagnosisCommandService
{
    /// <summary>Subflow 3.2 - Issue Diagnosis.</summary>
    Task<Result<NutritionalDiagnosis, NutritionalCareError>> Handle(IssueDiagnosisCommand command,
        CancellationToken cancellationToken = default);
}
