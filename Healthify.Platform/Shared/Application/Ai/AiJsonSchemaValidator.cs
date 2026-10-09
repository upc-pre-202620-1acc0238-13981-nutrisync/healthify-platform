using System.Text.Json;

namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>
///     IA-0, guard 6. Checks an output against the JSON Schema of its prompt. The provider is asked for that schema
///     natively, but its answer is still untrusted text, so it is checked again here before anyone reads it.
/// </summary>
/// <remarks>
///     Covers the subset that structured output accepts and that the prompts use: <c>type</c> (one or a list),
///     <c>properties</c>, <c>required</c>, <c>additionalProperties: false</c>, <c>items</c>, <c>enum</c>,
///     <c>minItems</c>/<c>maxItems</c>, <c>minLength</c>/<c>maxLength</c> and <c>minimum</c>/<c>maximum</c>. Any other
///     keyword is ignored, so a prompt must not rely on one for a safety rule: that belongs in its
///     <see cref="IAiOutputValidator{T}" />.
/// </remarks>
public static class AiJsonSchemaValidator
{
    /// <returns>The violations, each with its JSON path; empty when the instance is valid.</returns>
    public static IReadOnlyList<string> Validate(JsonElement instance, JsonElement schema)
    {
        var errors = new List<string>();
        Check(instance, schema, "$", errors);
        return errors;
    }

    private static void Check(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (schema.ValueKind != JsonValueKind.Object) return;

        if (schema.TryGetProperty("type", out var type) && !MatchesType(value, type))
        {
            errors.Add($"{path}: expected {type.GetRawText()}, found {value.ValueKind}.");
            return;
        }

        if (schema.TryGetProperty("enum", out var allowed) && allowed.ValueKind == JsonValueKind.Array &&
            !allowed.EnumerateArray().Any(a => JsonElement.DeepEquals(a, value)))
            errors.Add($"{path}: value not in enum.");

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                CheckObject(value, schema, path, errors);
                break;
            case JsonValueKind.Array:
                var count = value.GetArrayLength();
                if (Number(schema, "minItems") is { } minItems && count < minItems)
                    errors.Add($"{path}: fewer than {minItems} items.");
                if (Number(schema, "maxItems") is { } maxItems && count > maxItems)
                    errors.Add($"{path}: more than {maxItems} items.");
                if (schema.TryGetProperty("items", out var items))
                {
                    var index = 0;
                    foreach (var item in value.EnumerateArray()) Check(item, items, $"{path}[{index++}]", errors);
                }

                break;
            case JsonValueKind.String:
                var length = value.GetString()!.Length;
                if (Number(schema, "minLength") is { } minLength && length < minLength)
                    errors.Add($"{path}: shorter than {minLength}.");
                if (Number(schema, "maxLength") is { } maxLength && length > maxLength)
                    errors.Add($"{path}: longer than {maxLength}.");
                break;
            case JsonValueKind.Number:
                var number = value.GetDouble();
                if (Number(schema, "minimum") is { } minimum && number < minimum)
                    errors.Add($"{path}: below {minimum}.");
                if (Number(schema, "maximum") is { } maximum && number > maximum)
                    errors.Add($"{path}: above {maximum}.");
                break;
        }
    }

    private static void CheckObject(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        var hasProperties = schema.TryGetProperty("properties", out var properties) &&
                            properties.ValueKind == JsonValueKind.Object;

        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()))
                if (name is not null && !value.TryGetProperty(name, out _))
                    errors.Add($"{path}.{name}: required.");

        var closed = schema.TryGetProperty("additionalProperties", out var additional) &&
                     additional.ValueKind == JsonValueKind.False;

        foreach (var property in value.EnumerateObject())
            if (hasProperties && properties.TryGetProperty(property.Name, out var propertySchema))
                Check(property.Value, propertySchema, $"{path}.{property.Name}", errors);
            else if (closed)
                errors.Add($"{path}.{property.Name}: not allowed.");
    }

    private static bool MatchesType(JsonElement value, JsonElement type)
    {
        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Any(t => MatchesType(value, t.GetString()))
            : MatchesType(value, type.GetString());
    }

    private static bool MatchesType(JsonElement value, string? type)
    {
        return type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true
        };
    }

    private static double? Number(JsonElement schema, string keyword)
    {
        return schema.TryGetProperty(keyword, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    }
}
