using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-1. The baseline aggregate and its value objects: the closed medical condition list, the
///     height range, the plausible birth date and the age that updates itself on the birthday.
/// </summary>
public class PatientBaselineTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    [Theory]
    [InlineData(50)]
    [InlineData(168.25)]
    [InlineData(250)]
    public void Height_inside_the_range_is_accepted_and_rounded_to_one_decimal(decimal value)
    {
        var height = new HeightCm(value);

        Assert.Equal(decimal.Round(value, 1, MidpointRounding.AwayFromZero), height.Value);
    }

    [Theory]
    [InlineData(49.9)]
    [InlineData(250.1)]
    [InlineData(0)]
    public void Height_outside_the_range_is_rejected(decimal value)
    {
        Assert.Throws<ArgumentException>(() => new HeightCm(value));
    }

    [Theory]
    [InlineData("Hypothyroidism", "Hypothyroidism")]
    [InlineData("type2diabetes", "Type2Diabetes")]
    [InlineData(" Gout ", "Gout")]
    public void Medical_condition_from_the_closed_list_is_normalized(string input, string expected)
    {
        Assert.Equal(expected, new MedicalCondition(input).Value);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Other")]
    [InlineData("Asthma")]
    [InlineData("")]
    public void Medical_condition_outside_the_closed_list_is_rejected(string input)
    {
        Assert.Throws<ArgumentException>(() => new MedicalCondition(input));
    }

    [Fact]
    public void Empty_condition_list_is_valid_and_duplicates_collapse()
    {
        Assert.Empty(MedicalCondition.ListFrom([]));
        Assert.Empty(MedicalCondition.ListFrom(null));

        var conditions = MedicalCondition.ListFrom(["Gout", "gout", "Hypertension"]);

        Assert.Equal(["Gout", "Hypertension"], conditions.Select(c => c.Value));
    }

    [Fact]
    public void Age_updates_itself_on_the_birthday_without_writing_anything()
    {
        var baseline = new PatientBaseline(Record(new DateOnly(1995, 3, 10)), Today);
        var updatedBefore = baseline.UpdatedAt;

        Assert.Equal(30, baseline.AgeAt(new DateOnly(2026, 3, 9)));
        Assert.Equal(31, baseline.AgeAt(new DateOnly(2026, 3, 10)));
        Assert.Equal(31, baseline.AgeAt(new DateOnly(2027, 3, 9)));
        Assert.Equal(32, baseline.AgeAt(new DateOnly(2027, 3, 10)));

        // Nothing was stored: the age is derived, the birth date is the only fact.
        Assert.Equal(new DateOnly(1995, 3, 10), baseline.BirthDate);
        Assert.Equal(updatedBefore, baseline.UpdatedAt);
    }

    [Fact]
    public void A_29_February_birthday_counts_on_28_February_in_common_years()
    {
        var baseline = new PatientBaseline(Record(new DateOnly(2000, 2, 29)), Today);

        Assert.Equal(26, baseline.AgeAt(new DateOnly(2027, 2, 27)));
        Assert.Equal(27, baseline.AgeAt(new DateOnly(2027, 2, 28)));
        Assert.Equal(27, baseline.AgeAt(new DateOnly(2028, 2, 28)));
        Assert.Equal(28, baseline.AgeAt(new DateOnly(2028, 2, 29)));
    }

    [Theory]
    [InlineData(2025, 10, 6)] // less than one year
    [InlineData(2026, 10, 5)] // born today
    [InlineData(2030, 1, 1)] // in the future
    [InlineData(1906, 10, 4)] // more than 120 years
    public void Implausible_birth_date_is_rejected(int year, int month, int day)
    {
        Assert.Throws<ArgumentException>(() =>
            new PatientBaseline(Record(new DateOnly(year, month, day)), Today));
    }

    [Theory]
    [InlineData(2025, 10, 5)] // exactly one year
    [InlineData(1906, 10, 5)] // exactly 120 years
    public void Birth_date_on_the_limits_is_accepted(int year, int month, int day)
    {
        var baseline = new PatientBaseline(Record(new DateOnly(year, month, day)), Today);

        Assert.Equal(new DateOnly(year, month, day), baseline.BirthDate);
    }

    [Fact]
    public void Update_replaces_the_facts_and_keeps_who_recorded_it()
    {
        var baseline = new PatientBaseline(Record(new DateOnly(1995, 3, 10)), Today);

        baseline.Update(new UpdatePatientBaselineCommand(10, 99, new DateOnly(1995, 3, 11), "Male", 170.04m,
            ["Hypothyroidism"]), Today);

        Assert.Equal(new DateOnly(1995, 3, 11), baseline.BirthDate);
        Assert.Equal("Male", baseline.BiologicalSex.Value);
        Assert.Equal(170.0m, baseline.Height.Value);
        Assert.Equal(["Hypothyroidism"], baseline.Conditions.Select(c => c.Value));
        Assert.Equal(20, baseline.RecordedBy);
        Assert.False(baseline.BirthDateEstimated);
    }

    private static RecordPatientBaselineCommand Record(DateOnly birthDate)
    {
        return new RecordPatientBaselineCommand(10, 20, birthDate, "Female", 168m, ["Hypertension"]);
    }
}
