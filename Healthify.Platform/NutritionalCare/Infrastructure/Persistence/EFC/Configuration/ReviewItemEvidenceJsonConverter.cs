using System.Globalization;
using System.Text.Json.Nodes;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
///     NC-11. JSON shape of <c>review_items.evidence_data</c>:
///     <c>{ "averagePercentFromTarget": -40, "deviatedDays": 5, "loggedDaysConsidered": 9, "direction": "Below" }</c>,
///     plus <c>adjustedOn</c> (<c>yyyy-MM-dd</c>) and <c>adjustedPlanVersion</c> for a scheduled recheck (NC-10),
///     <c>averageEnergyKcalFromTarget</c> for a sustained deviation and <c>consistencyKgPerWeek</c>,
///     <c>consistencyState</c>, <c>alertSinceOn</c>, <c>weeksInAlert</c> and <c>shownToPatientOn</c> for a consistency
///     escalation (X-2). Absent properties are omitted.
/// </summary>
public static class ReviewItemEvidenceJsonConverter
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string Serialize(ReviewItemEvidence evidence)
    {
        var item = new JsonObject();
        if (evidence.AveragePercentFromTarget is not null)
            item["averagePercentFromTarget"] = evidence.AveragePercentFromTarget;
        if (evidence.DeviatedDays is not null) item["deviatedDays"] = evidence.DeviatedDays;
        if (evidence.LoggedDaysConsidered is not null) item["loggedDaysConsidered"] = evidence.LoggedDaysConsidered;
        if (evidence.Direction is not null) item["direction"] = evidence.Direction;
        if (evidence.AdjustedOn is not null)
            item["adjustedOn"] = evidence.AdjustedOn.Value.ToString(DateFormat, CultureInfo.InvariantCulture);
        if (evidence.AdjustedPlanVersion is not null) item["adjustedPlanVersion"] = evidence.AdjustedPlanVersion;
        if (evidence.AverageEnergyKcalFromTarget is not null)
            item["averageEnergyKcalFromTarget"] = evidence.AverageEnergyKcalFromTarget;
        if (evidence.ConsistencyKgPerWeek is not null) item["consistencyKgPerWeek"] = evidence.ConsistencyKgPerWeek;
        if (evidence.ConsistencyState is not null) item["consistencyState"] = evidence.ConsistencyState;
        if (evidence.AlertSinceOn is not null)
            item["alertSinceOn"] = evidence.AlertSinceOn.Value.ToString(DateFormat, CultureInfo.InvariantCulture);
        if (evidence.WeeksInAlert is not null) item["weeksInAlert"] = evidence.WeeksInAlert;
        if (evidence.ShownToPatientOn is not null)
            item["shownToPatientOn"] =
                evidence.ShownToPatientOn.Value.ToString(DateFormat, CultureInfo.InvariantCulture);
        return item.ToJsonString();
    }

    public static ReviewItemEvidence? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject item) return null;

        return new ReviewItemEvidence(
            item["averagePercentFromTarget"]?.GetValue<decimal>(),
            item["deviatedDays"]?.GetValue<int>(),
            item["loggedDaysConsidered"]?.GetValue<int>(),
            item["direction"]?.GetValue<string>(),
            DateOf(item, "adjustedOn"),
            item["adjustedPlanVersion"]?.GetValue<int>(),
            item["averageEnergyKcalFromTarget"]?.GetValue<decimal>(),
            item["consistencyKgPerWeek"]?.GetValue<decimal>(),
            item["consistencyState"]?.GetValue<string>(),
            DateOf(item, "alertSinceOn"),
            item["weeksInAlert"]?.GetValue<int>(),
            DateOf(item, "shownToPatientOn"));
    }

    private static DateOnly? DateOf(JsonObject item, string name)
    {
        return item[name]?.GetValue<string>() is { } date
            ? DateOnly.ParseExact(date, DateFormat, CultureInfo.InvariantCulture)
            : null;
    }
}
