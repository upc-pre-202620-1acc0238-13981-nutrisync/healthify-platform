namespace Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

/// <summary>
///     NC-3. Optional biochemistry of EV-2, in mg/dL. Each value is optional; the ranges only reject
///     what cannot be a human laboratory result, they are not clinical reference ranges.
/// </summary>
public sealed record BiochemistryPanel
{
    public BiochemistryPanel(decimal? fastingGlucoseMgDl, decimal? totalCholesterolMgDl,
        decimal? triglyceridesMgDl)
    {
        if (fastingGlucoseMgDl is < 20m or > 600m)
            throw new ArgumentException("Fasting glucose must be between 20 and 600 mg/dL.",
                nameof(fastingGlucoseMgDl));
        if (totalCholesterolMgDl is < 50m or > 500m)
            throw new ArgumentException("Total cholesterol must be between 50 and 500 mg/dL.",
                nameof(totalCholesterolMgDl));
        if (triglyceridesMgDl is < 20m or > 2000m)
            throw new ArgumentException("Triglycerides must be between 20 and 2000 mg/dL.",
                nameof(triglyceridesMgDl));
        if (fastingGlucoseMgDl is null && totalCholesterolMgDl is null && triglyceridesMgDl is null)
            throw new ArgumentException("A biochemistry panel needs at least one value.",
                nameof(fastingGlucoseMgDl));

        FastingGlucoseMgDl = Round(fastingGlucoseMgDl);
        TotalCholesterolMgDl = Round(totalCholesterolMgDl);
        TriglyceridesMgDl = Round(triglyceridesMgDl);
    }

    public decimal? FastingGlucoseMgDl { get; }
    public decimal? TotalCholesterolMgDl { get; }
    public decimal? TriglyceridesMgDl { get; }

    /// <summary>Null when nothing was recorded.</summary>
    public static BiochemistryPanel? From(decimal? fastingGlucoseMgDl, decimal? totalCholesterolMgDl,
        decimal? triglyceridesMgDl)
    {
        return fastingGlucoseMgDl is null && totalCholesterolMgDl is null && triglyceridesMgDl is null
            ? null
            : new BiochemistryPanel(fastingGlucoseMgDl, totalCholesterolMgDl, triglyceridesMgDl);
    }

    private static decimal? Round(decimal? value)
    {
        return value is null ? null : decimal.Round(value.Value, 1, MidpointRounding.AwayFromZero);
    }
}
