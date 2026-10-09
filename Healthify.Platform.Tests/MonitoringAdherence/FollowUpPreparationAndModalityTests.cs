using Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;
using Healthify.Platform.MonitoringAdherence.Domain.Model.Commands;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>MA-2. Preparation is a closed list and the modality is in person unless the practitioner says remote.</summary>
public class FollowUpPreparationAndModalityTests
{
    [Theory]
    [InlineData("Fasting")]
    [InlineData("lightclothing")]
    [InlineData(" BringBloodTests ")]
    [InlineData("EmptyBladder")]
    public void The_four_instructions_of_PR17_are_accepted_whatever_the_case(string value)
    {
        Assert.Contains(new PreparationInstruction(value).Value,
            new[] { "Fasting", "LightClothing", "BringBloodTests", "EmptyBladder" });
    }

    [Theory]
    [InlineData("Ayunas")]
    [InlineData("")]
    [InlineData("Fasting;LightClothing")]
    public void Anything_else_is_rejected(string value)
    {
        Assert.Throws<ArgumentException>(() => new PreparationInstruction(value));
    }

    [Fact]
    public void A_list_drops_duplicates_and_keeps_the_order_given()
    {
        var list = PreparationInstruction.ListOf(["LightClothing", "Fasting", "lightclothing"]);

        Assert.Equal(["LightClothing", "Fasting"], list.Select(p => p.Value));
        Assert.Empty(PreparationInstruction.ListOf(null));
    }

    [Theory]
    [InlineData(null, "InPerson")]
    [InlineData("", "InPerson")]
    [InlineData("remote", "Remote")]
    [InlineData("InPerson", "InPerson")]
    public void The_modality_defaults_to_in_person(string? value, string expected)
    {
        Assert.Equal(expected, new ConsultationModality(value).Value);
    }

    [Fact]
    public void An_unknown_modality_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ConsultationModality("Telefono"));
    }

    [Fact]
    public void A_new_visit_keeps_its_preparation_and_modality()
    {
        var followUp = new ScheduledFollowUp(new ScheduleFollowUpCommand(10, 20, DateTimeOffset.UtcNow.AddDays(3),
            ["Fasting", "BringBloodTests"], "Remote"));

        Assert.Equal(["Fasting", "BringBloodTests"], followUp.Preparation.Select(p => p.Value));
        Assert.Equal("Remote", followUp.Modality.Value);
        Assert.True(followUp.IsScheduled);
    }

    [Fact]
    public void Without_instructions_the_visit_has_none_and_is_in_person()
    {
        var followUp = new ScheduledFollowUp(new ScheduleFollowUpCommand(10, 20, DateTimeOffset.UtcNow.AddDays(3)));

        Assert.Empty(followUp.Preparation);
        Assert.Equal(ConsultationModality.InPerson, followUp.Modality.Value);
    }
}
