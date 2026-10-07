using Healthify.Platform.MonitoringAdherence.Domain.Services;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Clock;

/// <summary>
///     MA-2. Days of the agenda in <c>MonitoringAdherence:ClinicalTimeZone</c> (IANA id, default America/Lima). An
///     unknown zone falls back to the default, and if even that is unavailable, to UTC.
/// </summary>
/// <remarks>
///     NOTE: technical duplicate of the Nutritional Care clinical clock. This context cannot depend on that
///     context's services, so it reads its own key; both default to the same zone.
/// </remarks>
public class ClinicalTimeZoneFollowUpCalendar : IFollowUpCalendar
{
    public const string DefaultTimeZoneId = "America/Lima";

    private readonly TimeZoneInfo _timeZone;

    public ClinicalTimeZoneFollowUpCalendar(IConfiguration configuration,
        ILogger<ClinicalTimeZoneFollowUpCalendar> logger)
    {
        var configured = configuration["MonitoringAdherence:ClinicalTimeZone"];
        var id = string.IsNullOrWhiteSpace(configured) ? DefaultTimeZoneId : configured.Trim();
        var resolved = Resolve(id);
        if (resolved is null)
        {
            resolved = Resolve(DefaultTimeZoneId) ?? TimeZoneInfo.Utc;
            logger.LogWarning("Agenda time zone {Configured} is not available; using {Effective}.", id,
                resolved.Id);
        }

        _timeZone = resolved;
    }

    /// <summary>Test seam: a calendar fixed to one zone.</summary>
    public ClinicalTimeZoneFollowUpCalendar(TimeZoneInfo timeZone)
    {
        _timeZone = timeZone;
    }

    public (DateTimeOffset Start, DateTimeOffset End) LocalDayOf(DateTimeOffset moment)
    {
        var local = TimeZoneInfo.ConvertTime(moment, _timeZone);
        var midnight = local.Date;
        var start = new DateTimeOffset(midnight, _timeZone.GetUtcOffset(midnight));
        var nextMidnight = midnight.AddDays(1);
        var end = new DateTimeOffset(nextMidnight, _timeZone.GetUtcOffset(nextMidnight));
        return (start, end);
    }

    private static TimeZoneInfo? Resolve(string id)
    {
        try
        {
            // IANA ids resolve on every platform since .NET 6 (through ICU on Windows).
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }
}
