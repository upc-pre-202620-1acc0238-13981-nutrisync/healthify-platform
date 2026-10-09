using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Infrastructure.Calculators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-5. The parameters EV-4 calculates with by default, their configuration override and
///     DECISIÓN §12-#2 (no deficit for Underweight and NormalWeight).
/// </summary>
public class DefaultTargetParametersPolicyTests
{
    [Fact]
    public void Without_configuration_the_defaults_are_the_ones_of_EV_4()
    {
        var command = Policy().For(10, 20, new DiagnosisCode("OverweightGradeI"), new ActivityLevel("Moderate"),
            74.2m);

        Assert.Equal(10, command.PatientId);
        Assert.Equal(20, command.PractitionerId);
        Assert.Equal(Equation.MifflinStJeor, command.Equation);
        Assert.Equal(ReferenceWeight.Actual, command.ReferenceWeightKind);
        Assert.Equal(74.2m, command.ReferenceWeightKg);
        Assert.Equal(1.55m, command.ActivityFactor);
        Assert.Equal(DeficitStrategy.FixedKcal, command.DeficitKind);
        Assert.Equal(500m, command.DeficitValue);
        Assert.Equal(1.6m, command.ProteinGramsPerKg);
        Assert.Equal(30m, command.FatPercentOfEnergy);
    }

    [Theory]
    [InlineData("Underweight", 0)]
    [InlineData("NormalWeight", 0)]
    [InlineData("OverweightGradeI", 500)]
    [InlineData("ObesityGradeIII", 500)]
    public void No_weight_loss_is_proposed_at_or_below_a_normal_weight(string code, decimal expectedDeficit)
    {
        var command = Policy().For(10, 20, new DiagnosisCode(code), new ActivityLevel("Light"), 50m);

        Assert.Equal(expectedDeficit, command.DeficitValue);
    }

    [Fact]
    public void Configuration_overrides_each_value_and_activity_factors_come_from_their_own_section()
    {
        var command = Policy(new Dictionary<string, string?>
        {
            ["NutritionalCare:DefaultTargetParameters:Equation"] = "HarrisBenedict",
            ["NutritionalCare:DefaultTargetParameters:DeficitKind"] = "PercentOfTdee",
            ["NutritionalCare:DefaultTargetParameters:DeficitValue"] = "15",
            ["NutritionalCare:DefaultTargetParameters:ProteinGramsPerKg"] = "1.2",
            ["NutritionalCare:DefaultTargetParameters:FatPercentOfEnergy"] = "25",
            ["NutritionalCare:ActivityFactors:Moderate"] = "1.5"
        }).For(10, 20, new DiagnosisCode("ObesityGradeI"), new ActivityLevel("Moderate"), 90m);

        Assert.Equal(Equation.HarrisBenedict, command.Equation);
        Assert.Equal(DeficitStrategy.PercentOfTdee, command.DeficitKind);
        Assert.Equal(15m, command.DeficitValue);
        Assert.Equal(1.2m, command.ProteinGramsPerKg);
        Assert.Equal(25m, command.FatPercentOfEnergy);
        Assert.Equal(1.5m, command.ActivityFactor);
    }

    [Fact]
    public void Invalid_configuration_falls_back_to_the_defaults()
    {
        var command = Policy(new Dictionary<string, string?>
        {
            ["NutritionalCare:DefaultTargetParameters:Equation"] = "Magic",
            ["NutritionalCare:DefaultTargetParameters:ReferenceWeightKind"] = "Ideal",
            ["NutritionalCare:DefaultTargetParameters:DeficitKind"] = "Kcal",
            ["NutritionalCare:DefaultTargetParameters:ProteinGramsPerKg"] = "1,6",
            ["NutritionalCare:DefaultTargetParameters:FatPercentOfEnergy"] = "90"
        }).For(10, 20, new DiagnosisCode("OverweightGradeI"), new ActivityLevel("Moderate"), 74.2m);

        Assert.Equal(Equation.MifflinStJeor, command.Equation);
        Assert.Equal(ReferenceWeight.Actual, command.ReferenceWeightKind);
        Assert.Equal(DeficitStrategy.FixedKcal, command.DeficitKind);
        Assert.Equal(500m, command.DeficitValue);
        Assert.Equal(1.6m, command.ProteinGramsPerKg);
        Assert.Equal(30m, command.FatPercentOfEnergy);
    }

    internal static DefaultTargetParametersPolicy Policy(Dictionary<string, string?>? values = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();
        return new DefaultTargetParametersPolicy(configuration,
            new ConfiguredActivityFactorProvider(configuration, NullLogger<ConfiguredActivityFactorProvider>.Instance),
            NullLogger<DefaultTargetParametersPolicy>.Instance);
    }
}
