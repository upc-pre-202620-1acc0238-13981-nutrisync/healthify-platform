using System.Reflection;
using System.Text.Json;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>Builds nutrition plan versions as the database holds them.</summary>
public static class PlanScenario
{
    public static NutritionPlan Prescribed(int planId, int patientId, int practitionerId, int diagnosisId,
        int version)
    {
        var basis = new CalculationBasis(new Equation(Equation.MifflinStJeor), ReferenceWeight.Actual, 74.2m,
            1.55m, DeficitStrategy.FixedKcal, 500m, 1476m, 2287.8m);
        var plan = new NutritionPlan(patientId, practitionerId, diagnosisId, version, basis,
            new TargetProposal(1787.8m, 118.72m, 194.15m, 59.59m));
        plan.PrescribeTargets(new PrescribedTargets(1787.8m, 118.72m, 194.15m, 59.59m,
            new PrescriptionOutcome(PrescriptionOutcome.AcceptedAsProposed)));
        return Identity.Assign(plan, new PlanId(planId));
    }

    /// <summary>
    ///     A version published before NC-6 with free text lists, as it reads after the data migration
    ///     <c>NutritionalCare_GuidelineCodes</c>: the same rules the migration applies, column by column.
    /// </summary>
    public static NutritionPlan PublishedBeforeNc6(int planId, int patientId, int practitionerId, int diagnosisId,
        int version, IReadOnlyList<string> oldGuidelines, IReadOnlyList<string> oldRestrictions)
    {
        var plan = Prescribed(planId, patientId, practitionerId, diagnosisId, version);
        plan.PublishWith([], []);

        var (codes, legacy) = NutritionalCare_GuidelineCodes.ClassifyRestrictions(oldRestrictions);
        // The old JSON column, read by the tolerant converter, is what the migration turns into objects.
        SetField(plan, "_guidelines",
            GuidelineJsonConverter.Deserialize(JsonSerializer.Serialize(oldGuidelines)));
        SetField(plan, "_restrictions", codes);
        SetField(plan, "_legacyRestrictions", legacy);
        return plan;
    }

    private static void SetField(object target, string name, object value)
    {
        typeof(NutritionPlan).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }
}
