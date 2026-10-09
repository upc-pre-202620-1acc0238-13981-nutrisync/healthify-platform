using Healthify.Platform.CareRelationship.Domain.Model.Aggregates;
using Healthify.Platform.CareRelationship.Domain.Model.Commands;
using Healthify.Platform.CareRelationship.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.Commands;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;
using Healthify.Platform.MonitoringAdherence.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Repositories;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.ReadModels;

/// <summary>
///     NC-8, CR-3, MA-7 and RM-4 against a real MySQL (Category=MySql; skipped without HEALTHIFY_IT_MYSQL): the
///     columns of this session's migrations round-trip through the EF mappings (JSON converters, nullable dates,
///     the referral status default).
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "MySql")]
public class SessionMappingsMySqlTests
{
    [MySqlFact]
    public async Task Plan_changes_and_the_cached_contract_round_trip()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        int planId;
        await using (var context = db.NewContext())
        {
            var v1 = Published(1, [Guideline.FromCode(Guideline.PrioritizeVegetables)], 1796m);
            v1.RecordChangesFromPrevious(null);
            var v2 = Published(2,
                [Guideline.FromCode(Guideline.PrioritizeVegetables), Guideline.CustomText("Cena con verduras")], 1650m);
            v2.RecordChangesFromPrevious(v1);
            context.AddRange(v1, v2);

            context.Add(new ActiveTargetsCache(new RefreshActiveTargetsCacheCommand(10, 2, DateTimeOffset.UtcNow, 1650m,
                110m, 200m, 55m, ["PrioritizeVegetables"], [], null, null,
                [new CachedPlanChange("EnergyChanged", null, null, null, 1796m, 1650m)], "Probemos cenas más ligeras.")));
            await context.SaveChangesAsync();
            planId = v2.Id.Value;
        }

        await using (var context = db.NewContext())
        {
            var plan = await new NutritionPlanRepository(context).FindByIdAsync(planId);
            Assert.Equal(
            [
                PlanChange.EnergyWasChanged(1796m, 1650m),
                PlanChange.GuidelineWasAdded(Guideline.CustomText("Cena con verduras"))
            ], plan!.ChangesFromPrevious);
            Assert.Null(plan.PatientMessage);

            var cache = await new ActiveTargetsCacheRepository(context).FindByPatientIdAsync(10);
            var change = Assert.Single(cache!.ChangesFromPrevious!);
            Assert.Equal(("EnergyChanged", 1796m, 1650m), (change.Type, change.From, change.To));
            Assert.Equal("Probemos cenas más ligeras.", cache.PatientMessage);
        }
    }

    [MySqlFact]
    public async Task Acknowledgement_prompt_and_referral_status_round_trip()
    {
        await using var db = await MySqlIntegrationDatabase.CreateAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        int referralId;
        await using (var context = db.NewContext())
        {
            var link = new CareLink(new EstablishCareLinkCommand(10, 20, 1));
            link.GrantConsent(new GrantConsentCommand(1, 10, "diary,self-weigh-ins,active-targets"));
            link.MarkTargetsPending(1);
            link.AcknowledgeActiveTargets(1);
            context.Add(link);

            var index = new ConsistencyIndex(10);
            index.PromptPatient();
            context.Add(index);

            var referral = new Referral(new RecordReferralCommand(10, 20, "Endocrinología", "Control de tiroides"));
            context.Add(referral);
            await context.SaveChangesAsync();
            referralId = referral.Id.Value;
        }

        await using (var context = db.NewContext())
        {
            var link = await new CareLinkRepository(context).FindActiveByPatientIdAsync(10);
            Assert.NotNull(link!.LastAcknowledgedAt);

            var index = await new ConsistencyIndexRepository(context).FindByPatientIdAsync(10);
            Assert.True(index!.IsPatientPromptPending);
            index.MarkShownToPatient(DateTimeOffset.UtcNow);
            context.Update(index);

            var referral = await new ReferralRepository(context).FindByIdAsync(referralId);
            Assert.Equal(Referral.Open, referral!.Status);
            referral.Close(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        await using (var context = db.NewContext())
        {
            var index = await new ConsistencyIndexRepository(context).FindByPatientIdAsync(10);
            Assert.NotNull(index!.ShownToPatientAt);
            Assert.False(index.IsPatientPromptPending);
            var referral = await new ReferralRepository(context).FindByIdAsync(referralId);
            Assert.Equal((Referral.Closed, true), (referral!.Status, referral.ClosedAt is not null));
        }
    }

    private static NutritionPlan Published(int version, IReadOnlyList<Guideline> guidelines, decimal energy)
    {
        var basis = new CalculationBasis(new Equation(Equation.MifflinStJeor), ReferenceWeight.Actual, 74.2m, 1.55m,
            DeficitStrategy.FixedKcal, 500m, 1476m, 2287.8m);
        var plan = new NutritionPlan(10, 20, 1, version, basis, new TargetProposal(energy, 110m, 200m, 55m));
        plan.PrescribeTargets(new PrescribedTargets(energy, 110m, 200m, 55m,
            new PrescriptionOutcome(PrescriptionOutcome.AcceptedAsProposed)));
        plan.PublishWith(guidelines, []);
        return plan;
    }
}
