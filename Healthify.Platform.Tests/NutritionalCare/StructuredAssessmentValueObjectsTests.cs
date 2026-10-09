using Healthify.Platform.NutritionalCare.Domain.Model.Entities;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-3. Invariants of the structured assessment of EV-2: activity level, eating habits,
///     biochemistry, the protocol checklist, the plausibility ranges and the body mass index.
/// </summary>
public class StructuredAssessmentValueObjectsTests
{
    [Theory]
    [InlineData("Sedentary", 1.2)]
    [InlineData("light", 1.375)]
    [InlineData("Moderate", 1.55)]
    [InlineData("INTENSE", 1.725)]
    public void Activity_level_from_the_closed_list_carries_its_default_factor(string input, decimal factor)
    {
        var level = new ActivityLevel(input);

        Assert.Contains(level.Value, ActivityLevel.All);
        Assert.Equal(factor, level.DefaultFactor);
    }

    [Theory]
    [InlineData("VeryIntense")]
    [InlineData("Moderada")]
    [InlineData("")]
    public void Activity_level_outside_the_closed_list_is_rejected(string input)
    {
        Assert.Throws<ArgumentException>(() => new ActivityLevel(input));
    }

    [Fact]
    public void Eating_habits_are_optional_value_by_value()
    {
        Assert.Null(EatingHabits.From(null, null, null));

        var habits = EatingHabits.From(4, null, 2)!;

        Assert.Equal(4, habits.MealsPerDay);
        Assert.Null(habits.WaterLitersPerDay);
        Assert.Equal(2, habits.MealsOutPerWeek);
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(11, null, null)]
    [InlineData(null, -0.1, null)]
    [InlineData(null, 10.5, null)]
    [InlineData(null, null, -1)]
    [InlineData(null, null, 22)]
    public void Eating_habits_outside_their_ranges_are_rejected(int? meals, double? water, int? mealsOut)
    {
        Assert.Throws<ArgumentException>(() =>
            EatingHabits.From(meals, water is null ? null : (decimal)water.Value, mealsOut));
    }

    [Fact]
    public void Biochemistry_is_optional_value_by_value()
    {
        Assert.Null(BiochemistryPanel.From(null, null, null));

        var panel = BiochemistryPanel.From(92.04m, null, 150m)!;

        Assert.Equal(92.0m, panel.FastingGlucoseMgDl);
        Assert.Null(panel.TotalCholesterolMgDl);
        Assert.Equal(150m, panel.TriglyceridesMgDl);
    }

    [Theory]
    [InlineData(10, null, null)]
    [InlineData(700, null, null)]
    [InlineData(null, 20, null)]
    [InlineData(null, 900, null)]
    [InlineData(null, null, 5)]
    [InlineData(null, null, 3000)]
    public void Implausible_biochemistry_is_rejected(int? glucose, int? cholesterol, int? triglycerides)
    {
        Assert.Throws<ArgumentException>(() => BiochemistryPanel.From(glucose, cholesterol, triglycerides));
    }

    [Fact]
    public void Protocol_checklist_needs_at_least_one_check()
    {
        Assert.Throws<ArgumentException>(() => new MeasurementProtocolChecklist([]));
        Assert.Throws<ArgumentException>(() => new MeasurementProtocolChecklist(null));
    }

    [Fact]
    public void Protocol_checklist_rejects_unknown_checks()
    {
        Assert.Throws<ArgumentException>(() => new MeasurementProtocolChecklist(["Fasting", "Barefoot"]));
    }

    [Fact]
    public void Protocol_checklist_collapses_duplicates_and_keeps_the_canonical_order()
    {
        var checklist = new MeasurementProtocolChecklist(["SameScale", "fasting", "Fasting", "NoShoes"]);

        Assert.Equal(["Fasting", "NoShoes", "SameScale"], checklist.Checks);
        Assert.Equal("Fasting, NoShoes, SameScale", checklist.ToSummary());
    }

    [Theory]
    [InlineData(19.9, null, null)]
    [InlineData(350.1, null, null)]
    [InlineData(70, 39.9, null)]
    [InlineData(70, 200.1, null)]
    [InlineData(70, null, 2.9)]
    [InlineData(70, null, 70.1)]
    public void Implausible_measurement_is_rejected(double weight, double? waist, double? fat)
    {
        Assert.Throws<ArgumentException>(() => ClinicalMeasurement.EnsurePlausible((decimal)weight,
            waist is null ? null : (decimal)waist.Value, fat is null ? null : (decimal)fat.Value));
    }

    [Fact]
    public void Measurement_on_the_limits_is_plausible()
    {
        ClinicalMeasurement.EnsurePlausible(20m, 40m, 3m);
        ClinicalMeasurement.EnsurePlausible(350m, 200m, 70m);
    }

    [Fact]
    public void Bmi_is_weight_over_height_squared_with_one_decimal()
    {
        // EV-2: 74.2 kg and 168 cm show "IMC calculado 26.3 kg/m²".
        var bmi = new BodyMassIndex(74.2m, 168m);

        Assert.Equal(26.3m, bmi.Value);
        Assert.Equal(BodyMassIndex.OverweightGradeI, bmi.Category);
    }

    [Theory]
    [InlineData(18.4, "Underweight")]
    [InlineData(18.5, "NormalWeight")]
    [InlineData(24.9, "NormalWeight")]
    [InlineData(25.0, "OverweightGradeI")]
    [InlineData(29.9, "OverweightGradeI")]
    [InlineData(30.0, "ObesityGradeI")]
    [InlineData(34.9, "ObesityGradeI")]
    [InlineData(35.0, "ObesityGradeII")]
    [InlineData(39.9, "ObesityGradeII")]
    [InlineData(40.0, "ObesityGradeIII")]
    public void Bmi_category_follows_the_who_cut_offs(double bmi, string category)
    {
        Assert.Equal(category, BodyMassIndex.CategoryFor((decimal)bmi));
    }

    [Fact]
    public void Bmi_category_is_taken_from_the_rounded_value_so_both_agree()
    {
        // 1.80 m and 81.0 kg is 24.999...: shown as 25.0, so it must read OverweightGradeI.
        var bmi = new BodyMassIndex(80.99m, 180m);

        Assert.Equal(25.0m, bmi.Value);
        Assert.Equal(BodyMassIndex.OverweightGradeI, bmi.Category);
    }
}
