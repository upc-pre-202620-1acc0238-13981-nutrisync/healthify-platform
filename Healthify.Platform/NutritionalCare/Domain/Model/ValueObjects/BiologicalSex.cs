namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     The biological sex the equations are stratified by.
/// </summary>
/// <remarks>
///     NOTE: not listed among the value objects of this bounded context in the implementation
///     inventory, but three of the four mandated equations are undefined without it: Mifflin-St Jeor
///     applies a sex constant, Harris-Benedict has two whole formulas, and FAO/WHO/UNU is banded by
///     sex and age. It is a fact recorded during the assessment, never a clinical choice.
/// </remarks>
public sealed record BiologicalSex
{
    public const string Female = "Female";
    public const string Male = "Male";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { Female, Male };

    public BiologicalSex(string value)
    {
        if (!Allowed.Contains(value))
            throw new ArgumentException(
                $"'{value}' is not a valid biological sex. Allowed: {Female}, {Male}.", nameof(value));
        Value = Allowed.First(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public string Value { get; }

    public bool IsMale => Value == Male;

    public override string ToString()
    {
        return Value;
    }
}
