using System.Text.Json.Nodes;
using Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

namespace Healthify.Platform.IntakeBodyResponse.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
///     NC-6. JSON shape of the cached guidelines: <c>[{ "code": "ReduceSalt" }, { "custom": "Caminar 20 min" }]</c>.
///     A plain string element, the shape before NC-6, is read as a custom text.
/// </summary>
public static class CachedGuidelineJson
{
    public static string Serialize(List<CachedGuideline> guidelines)
    {
        var array = new JsonArray();
        foreach (var g in guidelines)
            array.Add(g.Code is not null
                ? new JsonObject { ["code"] = g.Code }
                : new JsonObject { ["custom"] = g.Custom });
        return array.ToJsonString();
    }

    public static List<CachedGuideline> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonArray array) return [];

        var result = new List<CachedGuideline>();
        foreach (var element in array)
            switch (element)
            {
                case JsonValue value when value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text):
                    result.Add(new CachedGuideline(null, text));
                    break;
                case JsonObject item:
                    var code = item["code"]?.GetValue<string>();
                    var custom = item["custom"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(custom))
                        result.Add(new CachedGuideline(code, string.IsNullOrWhiteSpace(code) ? custom : null));
                    break;
            }

        return result;
    }

    /// <summary>NC-8. <c>[{ "type": "GuidelineAdded", "code": "ReduceSalt" }, …]</c>, absent properties omitted.</summary>
    public static string SerializeChanges(List<CachedPlanChange> changes)
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

    public static List<CachedPlanChange> DeserializeChanges(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonArray array) return [];

        var result = new List<CachedPlanChange>();
        foreach (var element in array)
            if (element is JsonObject item && item["type"]?.GetValue<string>() is { } type &&
                !string.IsNullOrWhiteSpace(type))
                result.Add(new CachedPlanChange(type,
                    item["code"]?.GetValue<string>(),
                    item["custom"]?.GetValue<string>(),
                    item["macro"]?.GetValue<string>(),
                    item["from"]?.GetValue<decimal>(),
                    item["to"]?.GetValue<decimal>()));

        return result;
    }
}
