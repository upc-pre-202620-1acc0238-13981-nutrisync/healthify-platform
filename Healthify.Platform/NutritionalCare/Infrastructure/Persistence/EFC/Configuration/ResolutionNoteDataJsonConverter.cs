using System.Text.Json.Nodes;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
///     X-2. JSON shape of <c>review_items.resolution_note_data</c>: <c>{ "planVersion": 2 }</c>. Absent properties are
///     omitted; the backfill of <c>NutritionalCare_ReviewItemResolutionNoteCodes</c> writes the same shape.
/// </summary>
public static class ResolutionNoteDataJsonConverter
{
    public static string Serialize(ResolutionNoteData data)
    {
        var item = new JsonObject();
        if (data.PlanVersion is not null) item["planVersion"] = data.PlanVersion;
        return item.ToJsonString();
    }

    public static ResolutionNoteData? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject item) return null;
        return new ResolutionNoteData(item["planVersion"]?.GetValue<int>());
    }
}
