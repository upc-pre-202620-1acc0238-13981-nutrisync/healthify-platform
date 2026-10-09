using System.Globalization;
using System.Text.Json.Nodes;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
///     X-2. JSON shape of <c>nutrition_plans.change_reason_data</c>: <c>{ "date": "2026-09-18" }</c> for
///     NewConsultation, <c>{ "date": "2026-09-08", "signalType": "SustainedDeviation" }</c> for SignalAdjustment.
///     Absent properties are omitted; the backfill of <c>Nutrition_ChangeReasonCodes</c> writes the same shape.
/// </summary>
public static class ChangeReasonDataJsonConverter
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string Serialize(ChangeReasonData data)
    {
        var item = new JsonObject();
        if (data.Date is not null)
            item["date"] = data.Date.Value.ToString(DateFormat, CultureInfo.InvariantCulture);
        if (data.SignalType is not null) item["signalType"] = data.SignalType;
        return item.ToJsonString();
    }

    public static ChangeReasonData? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject item) return null;

        var date = item["date"]?.GetValue<string>() is { } text &&
                   DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None,
                       out var parsed)
            ? parsed
            : (DateOnly?)null;
        return new ChangeReasonData(date, item["signalType"]?.GetValue<string>());
    }
}
