using System.Globalization;

namespace Healthify.Platform.MonitoringAdherence.Infrastructure.Scheduling;

/// <summary>
///     IA-2. A five-field cron expression (minute, hour, day of month, month, day of week) evaluated on the clock of
///     a time zone: <c>0 6 * * 1</c> is every Monday at 06:00 in that zone. Each field takes <c>*</c>, a number, a
///     range <c>a-b</c>, a list <c>a,b</c> and a step <c>*/n</c> or <c>a-b/n</c>; day of week is 0–7 (0 and 7 are
///     Sunday). When both day fields are restricted, a day matching either runs, as in classic cron.
/// </summary>
/// <remarks>
///     NOTE: technical class. Written here rather than taken from a package: the platform needs one expression, and
///     a dependency for it is more surface than this.
/// </remarks>
public sealed class CronSchedule
{
    private const int SearchDays = 366 * 5;

    private readonly bool[] _minutes;
    private readonly bool[] _hours;
    private readonly bool[] _daysOfMonth;
    private readonly bool[] _months;
    private readonly bool[] _daysOfWeek;
    private readonly bool _dayOfMonthRestricted;
    private readonly bool _dayOfWeekRestricted;

    private CronSchedule(string expression, bool[] minutes, bool[] hours, bool[] daysOfMonth, bool[] months,
        bool[] daysOfWeek, bool dayOfMonthRestricted, bool dayOfWeekRestricted)
    {
        Expression = expression;
        _minutes = minutes;
        _hours = hours;
        _daysOfMonth = daysOfMonth;
        _months = months;
        _daysOfWeek = daysOfWeek;
        _dayOfMonthRestricted = dayOfMonthRestricted;
        _dayOfWeekRestricted = dayOfWeekRestricted;
    }

    public string Expression { get; }

    /// <exception cref="FormatException">The expression is not five valid fields.</exception>
    public static CronSchedule Parse(string expression)
    {
        var fields = (expression ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length != 5) throw new FormatException($"'{expression}' is not a five-field cron expression.");

        var daysOfWeek = Field(fields[4], 0, 7);
        // 7 is Sunday too.
        if (daysOfWeek[7]) daysOfWeek[0] = true;

        return new CronSchedule(string.Join(' ', fields), Field(fields[0], 0, 59), Field(fields[1], 0, 23),
            Field(fields[2], 1, 31), Field(fields[3], 1, 12), daysOfWeek, fields[2] != "*", fields[4] != "*");
    }

    /// <summary>The first moment strictly after <paramref name="after" /> that matches, or null within five years.</summary>
    public DateTimeOffset? NextAfter(DateTimeOffset after, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(after, timeZone).DateTime;
        // Whole minutes: the next candidate is the minute after the one we are in.
        var start = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0).AddMinutes(1);

        for (var day = 0; day < SearchDays; day++)
        {
            var date = start.Date.AddDays(day);
            if (!DayMatches(date)) continue;

            for (var hour = day == 0 ? start.Hour : 0; hour < 24; hour++)
            {
                if (!_hours[hour]) continue;
                var firstMinute = day == 0 && hour == start.Hour ? start.Minute : 0;
                for (var minute = firstMinute; minute < 60; minute++)
                {
                    if (!_minutes[minute]) continue;
                    var candidate = date.AddHours(hour).AddMinutes(minute);
                    // A local time that does not exist (a DST jump) never runs.
                    if (timeZone.IsInvalidTime(candidate)) continue;
                    return new DateTimeOffset(candidate, timeZone.GetUtcOffset(candidate));
                }
            }
        }

        return null;
    }

    /// <summary>The last moment at or before <paramref name="now" /> that matched, searching back <paramref name="window" />.</summary>
    public DateTimeOffset? PreviousAtOrBefore(DateTimeOffset now, TimeSpan window, TimeZoneInfo timeZone)
    {
        DateTimeOffset? previous = null;
        var cursor = now - window - TimeSpan.FromMinutes(1);
        while (NextAfter(cursor, timeZone) is { } next && next <= now)
        {
            previous = next;
            cursor = next;
        }

        return previous;
    }

    private bool DayMatches(DateTime date)
    {
        if (!_months[date.Month]) return false;
        var dayOfMonth = _daysOfMonth[date.Day];
        var dayOfWeek = _daysOfWeek[(int)date.DayOfWeek];
        if (_dayOfMonthRestricted && _dayOfWeekRestricted) return dayOfMonth || dayOfWeek;
        return dayOfMonth && dayOfWeek;
    }

    private static bool[] Field(string field, int minimum, int maximum)
    {
        var allowed = new bool[maximum + 1];
        foreach (var part in field.Split(','))
        {
            var (range, step) = part.Split('/') switch
            {
                [var r] => (r, 1),
                [var r, var s] => (r, Number(s, 1, maximum)),
                _ => throw new FormatException($"'{part}' is not a valid cron field.")
            };

            int from, to;
            if (range == "*")
            {
                (from, to) = (minimum, maximum);
            }
            else if (range.Split('-') is [var a, var b])
            {
                (from, to) = (Number(a, minimum, maximum), Number(b, minimum, maximum));
                if (to < from) throw new FormatException($"'{part}' is a reversed range.");
            }
            else
            {
                from = Number(range, minimum, maximum);
                to = step > 1 ? maximum : from;
            }

            for (var value = from; value <= to; value += step) allowed[value] = true;
        }

        return allowed;
    }

    private static int Number(string text, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < minimum ||
            value > maximum)
            throw new FormatException($"'{text}' is outside {minimum}-{maximum}.");
        return value;
    }
}
