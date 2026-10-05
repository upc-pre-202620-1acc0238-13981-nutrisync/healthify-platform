namespace Healthify.Platform.IntakeBodyResponse.Domain.Model.ValueObjects;

/// <summary>
///     The moment the patient declared on their own device.
/// </summary>
/// <remarks>
///     Business rule: Declared Local Timestamp Never Rewritten (Subflow 4.6). This is the one
///     timestamp in the platform the server does not own. It is what the patient says happened, and
///     the whole offline story depends on it surviving the journey unchanged: an entry logged at
///     eight in the evening with no signal is still an entry logged at eight in the evening when it
///     arrives at two in the morning.
///     The aggregates store it as a plain <see cref="DateTimeOffset" /> property literally named
///     <c>LocalTimestamp</c>, because that is the name the shared UTC interceptor excludes, and
///     because the diary is read by date range and a converted type would not translate to SQL.
/// </remarks>
public sealed record LocalTimestamp
{
    /// <summary>Device clocks drift. Beyond this the value is a mistake, not a skew.</summary>
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromHours(24);

    public LocalTimestamp(DateTimeOffset value)
    {
        // Business rule: Local Timestamp Required (Subflows 4.2 and 4.3)
        if (value == default)
            throw new ArgumentException("An entry must declare when it happened.", nameof(value));

        if (value > DateTimeOffset.UtcNow.Add(MaximumFutureSkew))
            throw new ArgumentException("A declared timestamp cannot be in the future.", nameof(value));

        Value = value;
    }

    public DateTimeOffset Value { get; }

    /// <summary>The calendar day the patient was living when they logged, not the server day.</summary>
    public DateOnly Date => DateOnly.FromDateTime(Value.Date);

    public override string ToString()
    {
        return Value.ToString("O");
    }
}

/// <summary>
///     How sure the on-device estimator was, from 0 to 1.
/// </summary>
/// <remarks>
///     Business rule: Confidence Always Attached (Subflow 4.2). An estimate without its confidence is
///     a number pretending to be a measurement. Every read model that shows an estimated intake shows
///     this beside it.
/// </remarks>
public sealed record Confidence
{
    public Confidence(decimal value)
    {
        if (value is < 0m or > 1m)
            throw new ArgumentException("Confidence must be between 0 and 1.", nameof(value));

        Value = decimal.Round(value, 4);
    }

    public decimal Value { get; }

    public override string ToString()
    {
        return Value.ToString("0.####");
    }
}

/// <summary>
///     A body weight in kilograms, inside the range a human body can occupy.
/// </summary>
/// <remarks>
///     Business rule: Plausible Weight Range (Subflow 4.5). The check is deliberately wide. Its job
///     is to catch a typed decimal point or a scale in pounds, not to judge the person standing on
///     the scale.
/// </remarks>
public sealed record WeightKg
{
    public const decimal Minimum = 20m;
    public const decimal Maximum = 400m;

    public WeightKg(decimal value)
    {
        if (value < Minimum || value > Maximum)
            throw new ArgumentException(
                $"A weight reading must be between {Minimum} and {Maximum} kilograms.", nameof(value));

        Value = decimal.Round(value, 2);
    }

    public decimal Value { get; }

    public override string ToString()
    {
        return Value.ToString("0.##");
    }
}
