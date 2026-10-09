namespace Healthify.Platform.NutritionalCare.Domain.Services;

/// <summary>
///     The calendar date in the time zone of the practice. Ages and consultation dates are clinical
///     facts of the practice's day, not of UTC: a birthday at 23:00 in Lima is already a birthday.
/// </summary>
public interface IClinicalDateProvider
{
    DateOnly Today();

    /// <summary>
    ///     NC-10. The practice's calendar date of an instant ("desviación sostenida del 8 sept."). The default reads
    ///     the instant in UTC; the configured provider reads it in the clinical time zone.
    /// </summary>
    DateOnly DateOf(DateTimeOffset instant)
    {
        return DateOnly.FromDateTime(instant.UtcDateTime);
    }
}
