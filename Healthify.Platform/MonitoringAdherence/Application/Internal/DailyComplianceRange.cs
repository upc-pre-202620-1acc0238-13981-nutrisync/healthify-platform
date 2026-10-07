using Healthify.Platform.MonitoringAdherence.Domain.Model.ValueObjects;

namespace Healthify.Platform.MonitoringAdherence.Application.Internal;

/// <summary>
///     MA-6. A range of calendar days of the Daily Compliance Indicator: every day (a day never evaluated is
///     Unlogged) and the same days counted. DECISIÓN §12-#7: the denominator is the calendar days of the range.
/// </summary>
public record DailyComplianceRange(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ComplianceDay> Days,
    ComplianceSummary Summary);
