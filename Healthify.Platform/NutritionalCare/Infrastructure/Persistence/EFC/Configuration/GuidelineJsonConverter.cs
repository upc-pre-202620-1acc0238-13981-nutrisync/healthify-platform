using System.Text.Json;
using System.Text.Json.Nodes;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
///     NC-6. JSON shape of the plan guidelines: <c>[{ "code": "ReduceSalt" }, { "custom": "Caminar 20 min" }]</c>.
/// </summary>
/// <remarks>
///     Reading is tolerant: a plain string element, the shape before NC-6, is read as a custom guideline,
///     exactly what the data migration <c>NutritionalCare_GuidelineCodes</c> turns it into. Stored rows are
///     rehydrated without validation, so history is read as it was written.
/// </remarks>
public static class GuidelineJsonConverter
{
    public static string Serialize(List<Guideline> guidelines)
    {
        var array = new JsonArray();
        foreach (var g in guidelines)
            array.Add(g.Code is not null
                ? new JsonObject { ["code"] = g.Code }
                : new JsonObject { ["custom"] = g.Custom });
        return array.ToJsonString();
    }

    public static List<Guideline> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        if (JsonNode.Parse(json) is not JsonArray array) return [];

        var result = new List<Guideline>();
        foreach (var element in array)
            switch (element)
            {
                case JsonValue value when value.TryGetValue<string>(out var text):
                    result.Add(Guideline.Rehydrate(null, text));
                    break;
                case JsonObject item:
                    var code = item["code"]?.GetValue<string>();
                    var custom = item["custom"]?.GetValue<string>();
                    if (code is not null || custom is not null) result.Add(Guideline.Rehydrate(code, custom));
                    break;
            }

        return result;
    }

    /// <summary>JSON list of strings, as the other list columns of the plan.</summary>
    public static string SerializeStrings(List<string> values)
    {
        return JsonSerializer.Serialize(values, (JsonSerializerOptions?)null);
    }

    public static List<string> DeserializeStrings(string? json)
    {
        return string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? [];
    }
}
