using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Domain.Services;
using Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-8. <see cref="PlanVersionDiff" />: "Qué cambió en esta versión", calculated once at publication from what
///     the patient receives (targets, guidelines and restrictions) and stored with the version.
/// </summary>
public class PlanVersionDiffTests
{
    private const int PatientId = 10;
    private const int PractitionerId = 20;

    [Fact]
    public void The_first_version_has_nothing_to_compare_against()
    {
        var first = Published(1, 1, [Guideline.FromCode(Guideline.ReduceSalt)], []);

        Assert.Empty(PlanVersionDiff.Compare(null, first));
    }

    [Fact]
    public void An_added_guideline_with_the_same_targets_reads_as_PT4()
    {
        // PT4: «Se agregó "Reduce la sal". Las metas de energía y macronutrientes no cambiaron.»
        var v2 = Published(1, 2, [Guideline.FromCode(Guideline.PrioritizeVegetables)], []);
        var v3 = Published(2, 3,
            [Guideline.FromCode(Guideline.PrioritizeVegetables), Guideline.FromCode(Guideline.ReduceSalt)], []);

        var changes = PlanVersionDiff.Compare(v2, v3);

        Assert.Equal(
            [PlanChange.NoTargetsChanged(), PlanChange.GuidelineWasAdded(Guideline.FromCode(Guideline.ReduceSalt))],
            changes);
    }

    [Fact]
    public void Energy_and_each_macro_are_reported_with_from_and_to()
    {
        var before = Published(1, 1, [], [], 1796m, 118.72m, 194.15m, 59.59m);
        var after = Published(2, 2, [], [], 1650m, 118.72m, 180m, 55m);

        var changes = PlanVersionDiff.Compare(before, after);

        Assert.Equal(
        [
            PlanChange.EnergyWasChanged(1796m, 1650m),
            PlanChange.MacroWasChanged(PlanChange.Carb, 194.15m, 180m),
            PlanChange.MacroWasChanged(PlanChange.Fat, 59.59m, 55m)
        ], changes);
        Assert.DoesNotContain(changes, c => c.Type == PlanChange.NoTargetChanges);
    }

    [Fact]
    public void Removed_guidelines_restrictions_and_legacy_restrictions_are_listed()
    {
        var before = PlanScenario.PublishedBeforeNc6(1, PatientId, PractitionerId, 1, 1,
            ["Caminar 20 min"], ["Vegan", "Sin cerdo"]);
        var after = Published(2, 2, [], [new DietaryRestriction(DietaryRestriction.GlutenFree)]);

        var changes = PlanVersionDiff.Compare(before, after);

        Assert.Equal(
        [
            PlanChange.NoTargetsChanged(),
            PlanChange.GuidelineWasRemoved(Guideline.CustomText("Caminar 20 min")),
            PlanChange.RestrictionWasAdded(DietaryRestriction.GlutenFree),
            PlanChange.RestrictionWasRemoved(DietaryRestriction.Vegan),
            PlanChange.LegacyRestrictionWasRemoved("Sin cerdo")
        ], changes);
    }

    [Fact]
    public void The_summary_is_recorded_once_at_publication_and_is_immutable()
    {
        var v1 = Published(1, 1, [], []);
        var draft = PlanScenario.Prescribed(2, PatientId, PractitionerId, 1, 2);

        Assert.Throws<InvalidOperationException>(() => draft.RecordChangesFromPrevious(v1));
        Assert.Null(draft.ChangesFromPrevious);

        draft.PublishWith([Guideline.FromCode(Guideline.ReduceSalt)], []);
        draft.RecordChangesFromPrevious(v1);

        Assert.NotNull(draft.ChangesFromPrevious);
        Assert.Throws<InvalidOperationException>(() => draft.RecordChangesFromPrevious(null));
    }

    [Fact]
    public void An_adjusted_version_records_what_it_changed_against_the_one_it_replaces()
    {
        var current = Published(1, 1, [Guideline.FromCode(Guideline.ReduceSalt)], []);

        var adjusted = current.CreateAdjustedVersion(
            new AdjustNutritionPlanCommand(1, PractitionerId, 1650m, 118.72m, 194.15m, 59.59m,
                [Guideline.ReduceSalt, Guideline.Drink2LWater], [], "Ajuste entre consultas"),
            new ChangeReason("Ajuste entre consultas"),
            [Guideline.FromCode(Guideline.ReduceSalt), Guideline.FromCode(Guideline.Drink2LWater)], []);

        Assert.Equal(
        [
            PlanChange.EnergyWasChanged(1787.8m, 1650m),
            PlanChange.GuidelineWasAdded(Guideline.FromCode(Guideline.Drink2LWater))
        ], adjusted.ChangesFromPrevious);
    }

    [Fact]
    public void The_stored_json_round_trips_and_omits_what_does_not_apply()
    {
        List<PlanChange> changes =
        [
            PlanChange.EnergyWasChanged(1796m, 1650m),
            PlanChange.MacroWasChanged(PlanChange.Protein, 118.72m, 120m),
            PlanChange.GuidelineWasAdded(Guideline.CustomText("Cena con verduras")),
            PlanChange.NoTargetsChanged()
        ];

        var json = PlanChangeJsonConverter.Serialize(changes);

        Assert.Equal(changes, PlanChangeJsonConverter.Deserialize(json));
        Assert.Contains("""{"type":"NoTargetChanges"}""", json);
        Assert.Contains("""{"type":"GuidelineAdded","custom":"Cena con verduras"}""", json);
    }

    private static NutritionPlan Published(int planId, int version, IReadOnlyList<Guideline> guidelines,
        IReadOnlyList<DietaryRestriction> restrictions, decimal energy = 1787.8m, decimal protein = 118.72m,
        decimal carb = 194.15m, decimal fat = 59.59m)
    {
        var basis = new CalculationBasis(new Equation(Equation.MifflinStJeor), ReferenceWeight.Actual, 74.2m,
            1.55m, DeficitStrategy.FixedKcal, 500m, 1476m, 2287.8m);
        var plan = new NutritionPlan(PatientId, PractitionerId, 1, version, basis,
            new TargetProposal(energy, protein, carb, fat));
        plan.PrescribeTargets(new PrescribedTargets(energy, protein, carb, fat,
            new PrescriptionOutcome(PrescriptionOutcome.AcceptedAsProposed)));
        plan.PublishWith(guidelines, restrictions);
        return Identity.Assign(plan, new PlanId(planId));
    }
}
