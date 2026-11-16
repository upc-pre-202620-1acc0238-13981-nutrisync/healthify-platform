using Healthify.Platform.MonitoringAdherence.Application.Internal;
using Microsoft.Extensions.Configuration;

namespace Healthify.Platform.Tests.MonitoringAdherence;

/// <summary>The reminder windows and switches come from configuration, with safe defaults.</summary>
public class FollowUpReminderSettingsTests
{
    private static FollowUpReminderSettings From(params (string Key, string? Value)[] values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v =>
                new KeyValuePair<string, string?>($"{FollowUpReminderSettings.SectionName}:{v.Key}", v.Value)))
            .Build();
        return FollowUpReminderSettings.From(config);
    }

    [Fact]
    public void Without_configuration_it_reminds_24_and_1_hours_before_only_the_patient()
    {
        var settings = From();

        Assert.True(settings.Enabled);
        Assert.Equal([60, 1440], settings.LeadMinutes);
        Assert.False(settings.NotifyPractitioner);
        Assert.Equal(10, settings.IntervalMinutes);
        Assert.Equal(200, settings.BatchSize);
        Assert.Null(settings.PracticeAddress);
        Assert.Null(settings.VideoCallUrl);
    }

    [Theory]
    [InlineData("48,24,2", new[] { 120, 1440, 2880 })]
    [InlineData("2", new[] { 120 })]
    [InlineData("0.5, 24", new[] { 30, 1440 })]
    [InlineData("24;1;24", new[] { 60, 1440 })]
    [InlineData("1 24", new[] { 60, 1440 })]
    public void Lead_hours_are_parsed_sorted_and_deduplicated(string raw, int[] expected)
    {
        Assert.Equal(expected, FollowUpReminderSettings.ParseLeadMinutes(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_value_means_the_default(string? raw)
    {
        Assert.Equal([60, 1440], FollowUpReminderSettings.ParseLeadMinutes(raw));
    }

    [Fact]
    public void Garbage_zero_and_negative_values_are_ignored()
    {
        Assert.Equal([180], FollowUpReminderSettings.ParseLeadMinutes("abc, 0, -4, 3"));
        Assert.Empty(FollowUpReminderSettings.ParseLeadMinutes("abc, 0, -4"));
    }

    [Fact]
    public void Switches_and_limits_are_read_and_bounded()
    {
        var settings = From(("Enabled", "false"), ("NotifyPractitioner", "true"), ("IntervalMinutes", "0"),
            ("BatchSize", "999999"), ("PracticeAddress", "  Av. Primavera 123  "),
            ("VideoCallUrl", "https://meet.example.com/abc"));

        Assert.False(settings.Enabled);
        Assert.True(settings.NotifyPractitioner);
        Assert.Equal(1, settings.IntervalMinutes);
        Assert.Equal(2000, settings.BatchSize);
        Assert.Equal("Av. Primavera 123", settings.PracticeAddress);
        Assert.Equal("https://meet.example.com/abc", settings.VideoCallUrl);
    }

    [Fact]
    public void A_placeholder_that_no_variable_replaced_counts_as_unset()
    {
        var settings = From(("PracticeAddress", "%PRACTICE_ADDRESS%"), ("VideoCallUrl", ""));

        Assert.Null(settings.PracticeAddress);
        Assert.Null(settings.VideoCallUrl);
    }
}
