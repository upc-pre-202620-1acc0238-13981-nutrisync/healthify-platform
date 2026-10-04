using System.Text.Json.Nodes;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
///     NC-8. JSON shape of <c>nutrition_plans.patient_change_summary</c>:
///     <c>[{ "type": "GuidelineAdded", "code": "ReduceSalt" }, { "type": "EnergyChanged", "from": 1796, "to": 1650 }]</c>.
///     Absent properties are omitted. Stored rows are rehydrated without validation.
/// </summary>
public static class PlanChangeJsonConverter
{
    public static string Serialize(List<PlanChange> changes)
    {
        var array = new JsonArray();
        foreach (var change in changes)
        {
            var item = new JsonObject { ["type"] = change.Type };
            if (change.Code is not null) item["code"] = change.Code;
            if (change.Custom is not null) item["custom"] = change.Custom;
            if (change.Macro is not null) item["macro"] = change.Macro;
            if (change.From is not null) item["from"] = change.From;
            if (change.To is not null) item["to"] = change.To;
            array.Add(item);
        }

        return array.ToJsonString();
    }

    public static List<PlanChange> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonArray array) return [];

        var result = new List<PlanChange>();
        foreach (var element in array)
            if (element is JsonObject item && item["type"]?.GetValue<string>() is { } type)
                result.Add(PlanChange.Rehydrate(type,
                    item["code"]?.GetValue<string>(),
                    item["custom"]?.GetValue<string>(),
                    item["macro"]?.GetValue<string>(),
                    item["from"]?.GetValue<decimal>(),
                    item["to"]?.GetValue<decimal>()));

        return result;
    }
}
