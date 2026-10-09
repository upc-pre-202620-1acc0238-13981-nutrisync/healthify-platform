using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.Shared.Application.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>
///     Each call runs in its own DI scope, with its own DbContext, as each HTTP request does in the API: nothing a
///     step tracked in memory can hide what the database really kept.
/// </summary>
public sealed class PerRequestConsultationService(IServiceProvider provider) : IConsultationCommandService
{
    public Task<Result<Consultation, NutritionalCareError>> Handle(StartConsultationCommand command,
        CancellationToken cancellationToken = default)
    {
        return Run(s => s.Handle(command, cancellationToken));
    }

    public Task<Result<ConsultationMeasurementOutcome, NutritionalCareError>> Handle(
        RecordConsultationMeasurementCommand command, CancellationToken cancellationToken = default)
    {
        return Run(s => s.Handle(command, cancellationToken));
    }

    public Task<Result<ConsultationDiagnosisOutcome, NutritionalCareError>> Handle(
        IssueConsultationDiagnosisCommand command, CancellationToken cancellationToken = default)
    {
        return Run(s => s.Handle(command, cancellationToken));
    }

    public Task<Result<ConsultationTargetProposalOutcome, NutritionalCareError>> Handle(
        ProposeConsultationTargetsCommand command, CancellationToken cancellationToken = default)
    {
        return Run(s => s.Handle(command, cancellationToken));
    }

    public Task<Result<ConsultationTargetsOutcome, NutritionalCareError>> Handle(
        PrescribeConsultationTargetsCommand command, CancellationToken cancellationToken = default)
    {
        return Run(s => s.Handle(command, cancellationToken));
    }

    public Task<Result<ConsultationPublicationOutcome, NutritionalCareError>> Handle(
        PublishFromConsultationCommand command, CancellationToken cancellationToken = default)
    {
        return Run(s => s.Handle(command, cancellationToken));
    }

    public Task<Result<Consultation, NutritionalCareError>> Handle(SaveConsultationPublicationDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        return Run(s => s.Handle(command, cancellationToken));
    }

    public Task<Result<Consultation, NutritionalCareError>> Handle(AbandonConsultationCommand command,
        CancellationToken cancellationToken = default)
    {
        return Run(s => s.Handle(command, cancellationToken));
    }

    private async Task<T> Run<T>(Func<IConsultationCommandService, Task<T>> call)
    {
        await using var scope = provider.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<IConsultationCommandService>());
    }
}
