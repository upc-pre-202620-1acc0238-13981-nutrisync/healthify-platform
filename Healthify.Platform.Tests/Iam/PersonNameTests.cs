using Healthify.Platform.Iam.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.Iam;

/// <summary>IAM-1. The person name is human text: required, trimmed, at most 80 characters, no digits.</summary>
public class PersonNameTests
{
    [Fact]
    public void Trims_and_collapses_inner_spaces()
    {
        var name = new PersonName("  María   José ", " Flores  Quispe ");

        Assert.Equal("María José", name.GivenNames);
        Assert.Equal("Flores Quispe", name.FamilyNames);
        Assert.Equal("María José Flores Quispe", name.FullName);
        Assert.Equal("María", name.FirstGivenName);
        Assert.Equal('M', name.Initial);
    }

    [Theory]
    [InlineData(null, "Flores")]
    [InlineData("", "Flores")]
    [InlineData("   ", "Flores")]
    [InlineData("Ana", null)]
    [InlineData("Ana", " ")]
    public void Both_parts_are_required(string? givenNames, string? familyNames)
    {
        Assert.Throws<ArgumentException>(() => new PersonName(givenNames, familyNames));
    }

    [Theory]
    [InlineData("Ana2", "Flores")]
    [InlineData("Ana", "<script>")]
    [InlineData("Ana", "Flores>")]
    public void Digits_and_angle_brackets_are_rejected(string givenNames, string familyNames)
    {
        Assert.Throws<ArgumentException>(() => new PersonName(givenNames, familyNames));
    }

    [Fact]
    public void Each_part_is_at_most_80_characters_after_normalising()
    {
        var eighty = new string('a', PersonName.MaxLength);

        Assert.Equal(eighty, new PersonName(eighty, "Flores").GivenNames);
        Assert.Throws<ArgumentException>(() => new PersonName(eighty + "b", "Flores"));
        Assert.Throws<ArgumentException>(() => new PersonName("Ana", eighty + "b"));
    }

    [Fact]
    public void The_initial_is_upper_case()
    {
        Assert.Equal('Á', new PersonName("álvaro", "Pérez").Initial);
    }
}
