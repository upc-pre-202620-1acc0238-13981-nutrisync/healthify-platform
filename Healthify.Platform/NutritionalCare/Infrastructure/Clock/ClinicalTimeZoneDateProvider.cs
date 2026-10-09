using Healthify.Platform.NutritionalCare.Domain.Services;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Clock;

/// <summary>
///     Today's date in <c>NutritionalCare:ClinicalTimeZone</c> (IANA id, default America/Lima). An unknown
///     zone falls back to the default, and if even that is unavailable, to UTC.
/// </summary>
public class ClinicalTimeZoneDateProvider : IClinicalDateProvider
{
    public const string DefaultTimeZoneId = "America/Lima";

    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    public ClinicalTimeZoneDateProvider(IConfiguration configuration,
        ILogger<ClinicalTimeZoneDateProvider> logger, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;

        var configured = configuration["NutritionalCare:ClinicalTimeZone"];
        var id = string.IsNullOrWhiteSpace(configured) ? DefaultTimeZoneId : configured.Trim();
        var resolved = Resolve(id);
        if (resolved is null)
        {
            resolved = Resolve(DefaultTimeZoneId) ?? TimeZoneInfo.Utc;
            logger.LogWarning("Clinical time zone {Configured} is not available; using {Effective}.", id,
                resolved.Id);
        }

        _timeZone = resolved;
    }

    public DateOnly Today()
    {
        return DateOf(_timeProvider.GetUtcNow());
    }

    public DateOnly DateOf(DateTimeOffset instant)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _timeZone).DateTime);
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
