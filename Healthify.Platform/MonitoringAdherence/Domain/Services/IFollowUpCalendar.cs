namespace Healthify.Platform.MonitoringAdherence.Domain.Services;

/// <summary>
///     MA-2. The calendar day of the practice a moment falls on. A visit at 18:00 in Lima and the consultation
///     published for it at 19:30 are the same day there, even when UTC has already turned the page.
/// </summary>
public interface IFollowUpCalendar
{
    /// <summary>The start (inclusive) and end (exclusive) of the local day of <paramref name="moment" />.</summary>
    (DateTimeOffset Start, DateTimeOffset End) LocalDayOf(DateTimeOffset moment);
}
