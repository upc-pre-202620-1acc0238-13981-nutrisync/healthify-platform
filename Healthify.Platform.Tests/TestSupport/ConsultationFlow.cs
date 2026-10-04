using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Shared.Application.Patterns;
using Xunit.Sdk;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     NC-2. Drives a guided consultation through its endpoints' command services, step by step, as EV-2 to EV-5
///     do. Each step must succeed; a failure stops the test with the step and its error.
/// </summary>
public static class ConsultationFlow
{
    public static async Task<int> StartAsync(IConsultationCommandService service, int patientId, int practitionerId)
    {
        return Ok(await service.Handle(new StartConsultationCommand(patientId, practitionerId)), "start").Id.Value;
    }

    public static async Task MeasureAsync(IConsultationCommandService service, int consultationId, int practitionerId,
        decimal weightKg)
    {
        Ok(await service.Handle(new RecordConsultationMeasurementCommand(consultationId, practitionerId, weightKg, 88m,
            null, ["Fasting", "NoShoes"], ActivityLevel.Moderate, null, null)), "measurement");
    }

    public static async Task<NutritionalDiagnosis> DiagnoseAsync(IConsultationCommandService service,
        int consultationId, int practitionerId, string code)
    {
        return Ok(await service.Handle(new IssueConsultationDiagnosisCommand(consultationId, practitionerId, code,
            DiagnosisSource.PractitionerSelected, null, null)), "diagnosis").Diagnosis;
    }

    public static async Task<NutritionPlan> TargetsAsync(IConsultationCommandService service, int consultationId,
        int practitionerId)
    {
        Ok(await service.Handle(new ProposeConsultationTargetsCommand(consultationId, practitionerId)), "proposal");
        return Ok(await service.Handle(new PrescribeConsultationTargetsCommand(consultationId, practitionerId,
            PrescriptionOutcome.AcceptedAsProposed)), "targets").Plan;
    }

    public static Task<Result<ConsultationPublicationOutcome, NutritionalCareError>> PublishAsync(
        IConsultationCommandService service, int consultationId, int practitionerId, string? idempotencyKey)
    {
        return service.Handle(new PublishFromConsultationCommand(consultationId, practitionerId,
            [DietaryRestriction.LactoseFree], [Guideline.ReduceSalt], ["Caminar 20 minutos"], idempotencyKey));
    }

    /// <summary>A whole consultation, from "Iniciar consulta" to "Publicar y cerrar consulta".</summary>
    public static async Task<ConsultationPublicationOutcome> CompleteAsync(IConsultationCommandService service,
        int patientId, int practitionerId, decimal weightKg, string diagnosisCode, string idempotencyKey)
    {
        var consultationId = await StartAsync(service, patientId, practitionerId);
        await MeasureAsync(service, consultationId, practitionerId, weightKg);
        await DiagnoseAsync(service, consultationId, practitionerId, diagnosisCode);
        await TargetsAsync(service, consultationId, practitionerId);
        return Ok(await PublishAsync(service, consultationId, practitionerId, idempotencyKey), "publication");
    }

    public static T Ok<T>(Result<T, NutritionalCareError> result, string step)
    {
        return result switch
        {
            Result<T, NutritionalCareError>.Success s => s.Value,
            Result<T, NutritionalCareError>.Failure f => throw new XunitException($"Step '{step}' failed: {f.Error}"),
            _ => throw new XunitException($"Step '{step}' returned no result.")
        };
    }
}
