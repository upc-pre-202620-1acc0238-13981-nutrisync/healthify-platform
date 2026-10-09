using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.Commands;
using Healthify.Platform.NutritionalCare.Infrastructure.Clock;
using Healthify.Platform.NutritionalCare.Interfaces.REST.Transform;
using Healthify.Platform.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-3. The clinical date is the practice's (NutritionalCare:ClinicalTimeZone, default America/Lima,
///     UTC-5 without daylight saving time), not UTC's: it decides ages and consultation dates.
/// </summary>
public class ClinicalDateTests
{
    private static readonly TimeSpan Lima = TimeSpan.FromHours(-5);
    private static readonly DateOnly BirthDate = new(1995, 3, 10);

    [Fact]
    public void A_birthday_at_23_00_in_Lima_already_counts()
    {
        // 23:00 in Lima on 10 March is 04:00 UTC on 11 March.
        var today = Provider(new DateTimeOffset(2026, 3, 10, 23, 0, 0, Lima)).Today();

        Assert.Equal(new DateOnly(2026, 3, 10), today);
        Assert.Equal(31, Baseline().AgeAt(today));
    }

    [Fact]
    public void The_evening_before_the_birthday_in_Lima_does_not_count_although_it_is_already_the_day_in_utc()
    {
        // 20:00 in Lima on 9 March is 01:00 UTC on 10 March: a UTC calendar would make the patient 31.
        var instant = new DateTimeOffset(2026, 3, 9, 20, 0, 0, Lima);
        var today = Provider(instant).Today();

        Assert.Equal(new DateOnly(2026, 3, 10), DateOnly.FromDateTime(instant.UtcDateTime));
        Assert.Equal(new DateOnly(2026, 3, 9), today);
        Assert.Equal(30, Baseline().AgeAt(today));
    }

    [Fact]
    public void The_baseline_resource_shows_the_age_of_the_practice_date()
    {
        var today = Provider(new DateTimeOffset(2026, 3, 10, 23, 0, 0, Lima)).Today();

        Assert.Equal(31, PatientBaselineResourceAssembler.ToResource(Baseline(), today).AgeYears);
    }

    [Fact]
    public void Another_time_zone_can_be_configured()
    {
        // 23:00 in Lima is already the next day in Madrid.
        var provider = Provider(new DateTimeOffset(2026, 3, 9, 23, 0, 0, Lima), "Europe/Madrid");

        Assert.Equal(new DateOnly(2026, 3, 10), provider.Today());
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("")]
    public void An_unknown_or_empty_time_zone_falls_back_to_Lima(string configured)
    {
        var provider = Provider(new DateTimeOffset(2026, 3, 9, 20, 0, 0, Lima), configured);

        Assert.Equal(new DateOnly(2026, 3, 9), provider.Today());
    }

    private static ClinicalTimeZoneDateProvider Provider(DateTimeOffset now, string? timeZone = null)
    {
        var values = new Dictionary<string, string?>();
        if (timeZone is not null) values["NutritionalCare:ClinicalTimeZone"] = timeZone;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ClinicalTimeZoneDateProvider(configuration, NullLogger<ClinicalTimeZoneDateProvider>.Instance,
            new FixedTimeProvider(now));
    }

    private static PatientBaseline Baseline()
    {
        return new PatientBaseline(new RecordPatientBaselineCommand(10, 20, BirthDate, "Female", 168m, []),
            new DateOnly(2026, 3, 1));
    }
}
