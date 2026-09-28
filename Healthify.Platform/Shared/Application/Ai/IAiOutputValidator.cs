namespace Healthify.Platform.Shared.Application.Ai;

/// <summary>
///     IA-0, guard 6. The business rules an output must meet once it is schema-valid JSON: kcal within the
///     remaining targets, no restricted ingredient, the calorie floor, no accusatory words, no diagnosis in a text
///     for the patient… Each context implements the validator of its own functions; the pipeline only runs it.
/// </summary>
public interface IAiOutputValidator<in T>
{
    /// <returns>The broken rules; empty when the output may be shown.</returns>
    IReadOnlyList<string> Validate(T output);
}

/// <summary>A validator without business rules, for outputs the schema already constrains completely.</summary>
public sealed class SchemaOnlyAiOutputValidator<T> : IAiOutputValidator<T>
{
    public static SchemaOnlyAiOutputValidator<T> Instance { get; } = new();

    public IReadOnlyList<string> Validate(T output)
    {
        return [];
    }
}
