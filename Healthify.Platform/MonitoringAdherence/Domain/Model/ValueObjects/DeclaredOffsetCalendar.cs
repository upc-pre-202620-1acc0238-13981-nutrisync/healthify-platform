using Healthify.Platform.MonitoringAdherence.Domain.Services;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

/// <summary>
///     The patient's own calendar on a day: the UTC offset their device declared when they logged it.
/// </summary>
/// <remarks>
///     Business rule: Compared Against That Day Snapshot (Subflow 5.4). The day a meal belongs to is the one the
///     patient declared, wherever they live, so the day a snapshot comes into force is read on the same clock.
///     Targets published at 20:00 in Lima are in force that evening in Lima and are already Tuesday in Madrid.
///     Only the offset crosses from the diary: no zone name, no place.
/// </remarks>
/// <param name="Offset">The offset declared on the device, between -14:00 and +14:00.</param>
public sealed record DeclaredOffsetCalendar(TimeSpan Offset) : IFollowUpCalendar
{
    private static readonly TimeSpan MaximumOffset = TimeSpan.FromHours(14);

    /// <summary>The offset declared on the device.</summary>
    public TimeSpan Offset { get; init; } = Offset.Duration() <= MaximumOffset
        ? Offset
        : throw new ArgumentException("A UTC offset is never beyond fourteen hours.", nameof(Offset));

    public (DateTimeOffset Start, DateTimeOffset End) LocalDayOf(DateTimeOffset moment)
    {
        var midnight = new DateTimeOffset(moment.ToOffset(Offset).Date, Offset);
        return (midnight, midnight.AddDays(1));
    }

    public DateOnly LocalDateOf(DateTimeOffset moment)
    {
        return DateOnly.FromDateTime(moment.ToOffset(Offset).DateTime);
    }
}
