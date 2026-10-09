namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     The patient's height in centimetres, recorded once in the baseline and not asked again at
///     every consultation.
/// </summary>
/// <remarks>Rounded to one decimal, which is the precision of a stadiometer reading.</remarks>
public sealed record HeightCm
{
    public const decimal Minimum = 50m;
    public const decimal Maximum = 250m;

    public HeightCm(decimal value)
    {
        if (value is < Minimum or > Maximum)
            throw new ArgumentException($"The height must be between {Minimum} and {Maximum} cm.",
                nameof(value));
        Value = decimal.Round(value, 1, MidpointRounding.AwayFromZero);
    }

    public decimal Value { get; }

    public override string ToString()
    {
        return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
