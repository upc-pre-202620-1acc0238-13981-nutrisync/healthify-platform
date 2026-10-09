using Cortex.Mediator;
using Healthify.Platform.CareRelationship.Interfaces.Acl;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Interfaces.Acl;
using Healthify.Platform.NutritionalCare.Application.CommandServices;
using Healthify.Platform.NutritionalCare.Application.Internal;
using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.Entities;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.NutritionalCare.Infrastructure.Scheduling;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Application.Patterns;
using Healthify.Platform.Shared.Domain.Repositories;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-10 against a real MySQL server: accepting a plan proposal writes the new version, the superseded one and the
///     resolved item in one transaction. When the save of the item fails after the versions were written, nothing
///     remains: the version in force stays in force, there is no v2, the item stays open with its proposal Proposed.
///     Then the same acceptance succeeds and everything reads back from the database (NC-10 and NC-11 columns).
/// </summary>
/// <remarks>Skipped unless <c>HEALTHIFY_IT_MYSQL</c> is set (CLAUDE.md §5).</remarks>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class PlanProposalAcceptanceMySqlTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [MySqlFact]
    public async Task A_failure_halfway_through_the_acceptance_rolls_everything_back()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        await using (var scope = db.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPatientBaselineRepository>().AddAsync(
                ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
                    Today));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }

        var v1 = (await ConsultationFlow.CompleteAsync(db.Consultations, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication")).Plan;
        var reviewItemId = await SeedItemWithProposalAsync(db, v1);

        // The versions are written by the first save; the item, by the second one, which fails.
        db.Failure.FailOn = "review_item";
        var failed = await AcceptAsync(db, reviewItemId);
        db.Failure.FailOn = null;

        Assert.Equal(NutritionalCareError.UnexpectedError,
            Assert.IsType<Result<PlanProposalAcceptanceOutcome, NutritionalCareError>.Failure>(failed).Error);
        await using (var context = db.NewContext())
        {
            var plans = await context.Set<NutritionPlan>().Where(p => p.PatientId == PatientId).ToListAsync();
            var only = Assert.Single(plans);
            Assert.Equal(1, only.Version);
            Assert.True(only.IsActive);
            Assert.Null(only.SupersededAt);

            var item = await context.Set<ReviewItem>().Include(r => r.Proposal)
                .SingleAsync(r => r.Id == new ReviewItemId(reviewItemId));
            Assert.True(item.IsOpen);
            Assert.Null(item.RecheckDueAt);
            Assert.Equal(PlanProposalStatus.Proposed, item.Proposal!.Status.Value);
            Assert.Null(item.Proposal.AssignedPlanVersion);

            Assert.Equal(1, (await context.Set<ActiveTargetsCache>().SingleAsync(c => c.PatientId == PatientId))
                .PlanVersion);
        }

        // The same acceptance, now without the failure.
        var accepted = await AcceptAsync(db, reviewItemId);
        Assert.True(accepted.IsSuccess);
        await using (var context = db.NewContext())
        {
            var plans = await context.Set<NutritionPlan>().Where(p => p.PatientId == PatientId)
                .OrderBy(p => p.Version).ToListAsync();
            Assert.Equal([1, 2], plans.Select(p => p.Version));
            Assert.False(plans[0].IsActive);
            Assert.True(plans[1].IsActive);
            Assert.Equal("Notamos que tus cenas son más ligeras.", plans[1].PatientMessage);
            Assert.StartsWith("Ajuste por señal: desviación sostenida del", plans[1].ChangeReason!.Value);

            var item = await context.Set<ReviewItem>().Include(r => r.Proposal)
                .SingleAsync(r => r.Id == new ReviewItemId(reviewItemId));
            Assert.False(item.IsOpen);
            Assert.True(item.ResolvedWithAdjustment);
            Assert.NotNull(item.RecheckDueAt);
            Assert.Equal(PlanProposalStatus.AcceptedAsIs, item.Proposal!.Status.Value);
            Assert.Equal(2, item.Proposal.AssignedPlanVersion);
            Assert.Equal([Guideline.ProteinAndVegetablesAtDinner], item.Proposal.AddedGuidelines);
            Assert.Empty(item.Proposal.RemovedGuidelines);
            Assert.Equal(new ReviewItemEvidence(-40m, 5, 9, "Below"), item.EvidenceData);

            var cache = await context.Set<ActiveTargetsCache>().SingleAsync(c => c.PatientId == PatientId);
            Assert.Equal(2, cache.PlanVersion);
            Assert.Equal("Notamos que tus cenas son más ligeras.", cache.PatientMessage);
        }
    }

    /// <summary>
    ///     IA-8. The patient's AI processing ended: the unaccepted proposal row is deleted from
    ///     <c>review_item_plan_proposals</c>, the accepted one stays, and both review items stay.
    /// </summary>
    [MySqlFact]
    public async Task Purging_deletes_the_pending_proposal_row_and_keeps_the_accepted_one()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(Today);
        await using (var scope = db.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPatientBaselineRepository>().AddAsync(
                ConsultationScenario.Baseline(PatientId, PractitionerId, new DateOnly(1995, 3, 10), "Female", 168m,
                    Today));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        }

        var v1 = (await ConsultationFlow.CompleteAsync(db.Consultations, PatientId, PractitionerId, 80m,
            DiagnosisCode.ObesityGradeI, "first-publication")).Plan;
        var acceptedId = await SeedItemWithProposalAsync(db, v1);
        Assert.True((await AcceptAsync(db, acceptedId)).IsSuccess);
        var pendingId = await SeedItemWithProposalAsync(db, v1);

        await using (var scope = db.NewScope())
        {
            var service = new ReviewItemCommandService(
                scope.ServiceProvider.GetRequiredService<IReviewItemRepository>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                Substitute.For<ICareRelationshipContextFacade>(),
                NullLogger<ReviewItemCommandService>.Instance, Substitute.For<IMediator>(),
                scope.ServiceProvider.GetRequiredService<PlanAdjustmentInputReader>(),
                Substitute.For<IPlanAdjustmentProposer>(), Substitute.For<IMonitoringContextFacade>(),
                new FixedClinicalDate(Today), TimeProvider.System, new InMemoryPlanProposalGenerationQueue(),
                Substitute.For<IAiSettings>(), Substitute.For<IAiConsentPolicy>());
            var purged = await service.Handle(new PurgeUnacceptedPlanProposalsCommand(PatientId));
            Assert.Equal(1, Assert.IsType<Result<int, NutritionalCareError>.Success>(purged).Value);
        }

        await using (var context = db.NewContext())
        {
            var proposals = await context.Set<PlanAdjustmentProposal>().ToListAsync();
            var kept = Assert.Single(proposals);
            Assert.Equal(new ReviewItemId(acceptedId), kept.ReviewItemId);
            Assert.Equal(PlanProposalStatus.AcceptedAsIs, kept.Status.Value);
            Assert.Equal(1L, await context.Database
                .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM review_item_plan_proposals").SingleAsync());

            var items = await context.Set<ReviewItem>().Include(r => r.Proposal).OrderBy(r => r.Id).ToListAsync();
            Assert.Equal(2, items.Count);
            var pending = items.Single(r => r.Id == new ReviewItemId(pendingId));
            Assert.True(pending.IsOpen);
            Assert.Null(pending.Proposal);
            Assert.Equal(new ReviewItemEvidence(-40m, 5, 9, "Below"), pending.EvidenceData);
        }
    }

    private static async Task<int> SeedItemWithProposalAsync(MySqlIntegrationDatabase db, NutritionPlan plan)
    {
        await using var scope = db.NewScope();
        var targets = plan.PrescribedTargets!;
        var energy = decimal.Round(targets.EnergyKcal * 0.92m, 0);
        var protein = decimal.Round(targets.ProteinG, 0);
        var fat = decimal.Round(targets.FatG * 0.92m, 0);
        var carb = decimal.Round((energy - 4m * protein - 9m * fat) / 4m, 0);

        var item = new ReviewItem(new OpenReviewItemCommand(PatientId, SignalType.SustainedDeviation,
            "Mean energy 40.0% below", new ReviewItemEvidenceDto(-40m, 5, 9, "Below")), PractitionerId);
        item.AttachProposal(1, "Ajustar la energía y reforzar las cenas", energy, protein, carb, fat,
            [Guideline.ProteinAndVegetablesAtDinner], [], "Notamos que tus cenas son más ligeras.", 7,
            "Registró 40 % menos de su meta en 5 de 9 días.", DateTimeOffset.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IReviewItemRepository>().AddAsync(item);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CompleteAsync();
        return item.Id.Value;
    }

    private static async Task<Result<PlanProposalAcceptanceOutcome, NutritionalCareError>> AcceptAsync(
        MySqlIntegrationDatabase db, int reviewItemId)
    {
        // A request scope per call, as each POST.
        await using var scope = db.NewScope();
        return await scope.ServiceProvider.GetRequiredService<IPlanProposalCommandService>()
            .Handle(new AcceptPlanProposalCommand(reviewItemId, PractitionerId, true, null));
    }
}
