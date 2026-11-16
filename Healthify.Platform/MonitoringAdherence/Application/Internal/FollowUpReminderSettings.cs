namespace Healthify.Platform.MonitoringAdherence.Application.Internal;

/// <summary>
///     The configuration of the appointment reminders, read from <c>Reminders:FollowUp</c>.
/// </summary>
/// <remarks>
///     <c>Enabled</c> (default true; without SMTP nothing is sent anyway), <c>LeadHours</c> (comma-separated hours
///     before the visit, default <c>24,1</c>; decimals allowed, e.g. <c>0.5</c>), <c>NotifyPractitioner</c> (default
///     false), <c>IntervalMinutes</c> (how often the worker looks, default 10), <c>BatchSize</c> (default 200),
///     <c>PracticeAddress</c> and <c>VideoCallUrl</c> (optional: shown for in-person and remote visits).
/// </remarks>
public sealed record FollowUpReminderSettings(
    bool Enabled,
    IReadOnlyList<int> LeadMinutes,
    bool NotifyPractitioner,
    int IntervalMinutes,
    int BatchSize,
    string? PracticeAddress,
    string? VideoCallUrl)
{
    public const string SectionName = "Reminders:FollowUp";
    public const string DefaultLeadHours = "24,1";

    public static FollowUpReminderSettings From(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new FollowUpReminderSettings(
            section.GetValue<bool?>("Enabled") ?? true,
            ParseLeadMinutes(section["LeadHours"]),
            section.GetValue<bool?>("NotifyPractitioner") ?? false,
            Math.Max(1, section.GetValue<int?>("IntervalMinutes") ?? 10),
            Math.Clamp(section.GetValue<int?>("BatchSize") ?? 200, 1, 2000),
            Clean(section["PracticeAddress"]),
            Clean(section["VideoCallUrl"]));
    }

    /// <summary>«24,1» → [60, 1440]. Anything that is not a positive number is ignored; empty means the default.</summary>
    public static IReadOnlyList<int> ParseLeadMinutes(string? leadHours)
    {
        var raw = string.IsNullOrWhiteSpace(leadHours) ? DefaultLeadHours : leadHours;
        return raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => double.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var hours)
                ? (int)Math.Round(hours * 60)
                : 0)
            .Where(minutes => minutes > 0)
            .Distinct()
            .OrderBy(minutes => minutes)
            .ToList();
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || (value.StartsWith('%') && value.EndsWith('%'))
            ? null
            : value.Trim();
    }
}
