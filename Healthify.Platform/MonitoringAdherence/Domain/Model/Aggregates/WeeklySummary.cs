using System.Text.Json;
using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Domain.Model.Aggregates;

/// <summary>
///     The weekly summary the patient reads ("Your week", with AI): a headline, what went
///     well and what to look at, written by the AI from facts computed without it.
/// </summary>
/// <remarks>
///     Business rule: One Summary Per Patient And Week, backed by a unique index on (patient_id, week_start).
///     Business rule: The AI Does Not Count. <see cref="ComplianceFacts" /> holds the figures the texts were
///     checked against; the texts never carry a number that is not there.
///     Business rule: Summary Needs Three Logged Days. With fewer, nothing is generated and the patient sees
///     that there is no summary yet.
///     Only the patient reads it; the practitioner has the monitoring summary. It is generated content:
///     withdrawing the AI consent or turning the function off deletes it, and it expires with the retention of
///     the AI audit.
/// </remarks>
public partial class WeeklySummary
{
    /// <summary>Rule: Summary Needs Three Logged Days.</summary>
    public const int MinimumLoggedDays = 3;

    public const int HeadlineMaximumLength = 200;
    public const int BulletMaximumLength = 240;
    public const int MaximumWentWell = 3;
    public const int MaximumWatchOut = 2;

    private static readonly JsonSerializerOptions FactsOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Languages = new(StringComparer.Ordinal) { "es", "en" };

    private List<string> _wentWell = [];
    private List<string> _watchOut = [];
    private string _complianceFactsJson = "{}";
    private MonitoringPeriodFacts? _complianceFacts;

    /// <summary>Required by EF Core.</summary>
    protected WeeklySummary()
    {
    }

    /// <summary>Generate Weekly Summary, once the output was validated and audited.</summary>
    /// <exception cref="ArgumentException">When the week, the facts or the texts break a rule of the summary.</exception>
    public WeeklySummary(int patientId, MonitoringPeriodFacts facts, string headline, IEnumerable<string> wentWell,
        IEnumerable<string>? watchOut, long aiGenerationId, string language, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (patientId <= 0) throw new ArgumentException("A summary belongs to a patient.", nameof(patientId));
        if (!IsWeekStart(facts.From) || facts.To != facts.From.AddDays(6))
            throw new ArgumentException("A weekly summary covers one week, Monday to Sunday.", nameof(facts));
        // Business rule: Summary Needs Three Logged Days.
        if (facts.LoggedDays < MinimumLoggedDays)
            throw new ArgumentException(
                $"A weekly summary needs at least {MinimumLoggedDays} logged days.", nameof(facts));
        if (aiGenerationId <= 0)
            throw new ArgumentException("A summary is traced to its AI generation.", nameof(aiGenerationId));
        if (language is null || !Languages.Contains(language))
            throw new ArgumentException("The language of a summary is es or en.", nameof(language));

        var title = Text(headline, HeadlineMaximumLength, nameof(headline));
        var well = (wentWell ?? []).Select(w => Text(w, BulletMaximumLength, nameof(wentWell))).ToList();
        var watch = (watchOut ?? []).Select(w => Text(w, BulletMaximumLength, nameof(watchOut))).ToList();
        if (well.Count is < 1 or > MaximumWentWell)
            throw new ArgumentException($"A summary says 1 to {MaximumWentWell} things that went well.",
                nameof(wentWell));
        if (watch.Count > MaximumWatchOut)
            throw new ArgumentException($"A summary points at most {MaximumWatchOut} things to look at.",
                nameof(watchOut));

        PatientId = patientId;
        WeekStart = facts.From;
        WeekEnd = facts.To;
        _complianceFactsJson = JsonSerializer.Serialize(facts, FactsOptions);
        _complianceFacts = facts;
        Headline = title;
        _wentWell = well;
        _watchOut = watch;
        AiGenerationId = aiGenerationId;
        Language = language;
        GeneratedAt = generatedAt;
    }

    public WeeklySummaryId Id { get; private set; } = null!;

    /// <summary>Cross-context reference to the patient. A plain int, no EF navigation.</summary>
    public int PatientId { get; private set; }

    /// <summary>Monday of the week ("week of 8 to 14 September").</summary>
    public DateOnly WeekStart { get; private set; }

    /// <summary>Sunday of the week.</summary>
    public DateOnly WeekEnd { get; private set; }

    /// <summary>The deterministic facts the texts were written from and checked against.</summary>
    public MonitoringPeriodFacts ComplianceFacts =>
        _complianceFacts ??= JsonSerializer.Deserialize<MonitoringPeriodFacts>(_complianceFactsJson, FactsOptions)!;

    /// <summary>"You met your targets 5 of 7 days."</summary>
    public string Headline { get; private set; } = null!;

    /// <summary>What went well: one to three bullets.</summary>
    public IReadOnlyList<string> WentWell => _wentWell.ToList();

    /// <summary>What to look at: up to two bullets.</summary>
    public IReadOnlyList<string> WatchOut => _watchOut.ToList();

    /// <summary>The <c>ai_generations</c> row that wrote it. Not published: it stays for the audit.</summary>
    public long AiGenerationId { get; private set; }

    /// <summary>es or en: the patient's language when it was generated.</summary>
    public string Language { get; private set; } = null!;

    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>Monday of the week of <paramref name="date" />.</summary>
    public static DateOnly WeekStartOf(DateOnly date)
    {
        return date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    }

    public static bool IsWeekStart(DateOnly date)
    {
        return date.DayOfWeek == DayOfWeek.Monday;
    }

    private static string Text(string? value, int maximumLength, string parameter)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > maximumLength)
            throw new ArgumentException($"A text of the summary has 1 to {maximumLength} characters.", parameter);
        return text;
    }
}
