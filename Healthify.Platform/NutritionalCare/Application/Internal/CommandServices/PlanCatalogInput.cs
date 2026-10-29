using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;

namespace Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;

/// <summary>
///     Step 1 of the command services that publish a plan version: builds the guidelines and
///     restrictions and maps each failure to its own error, before anything is loaded.
/// </summary>
public static class PlanCatalogInput
{
    /// <summary>
    ///     Plan adjustment: a catalog code in <paramref name="guidelines" /> is that code and any other
    ///     text a custom guideline (compatibility with the original free text list); <paramref name="custom" />
    ///     are always custom.
    /// </summary>
    public static NutritionalCareError? TryGuidelines(IReadOnlyList<string>? guidelines,
        IReadOnlyList<string>? custom, out List<Guideline> result)
    {
        result = [];
        try
        {
            result.AddRange(NonBlank(guidelines).Select(Guideline.FromLegacyText));
            result.AddRange(NonBlank(custom).Select(Guideline.CustomText));
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.InvalidCustomGuideline;
        }

        result = result.Distinct().ToList();
        // Business rule: At Most Five Custom Guidelines Per Version
        return result.Count(g => g.IsCustom) > Guideline.MaximumCustomPerVersion
            ? NutritionalCareError.TooManyCustomGuidelines
            : null;
    }

    /// <summary>
    ///     Guided consultation: <paramref name="codes" /> must all be in the catalog; the custom
    ///     texts arrive apart.
    /// </summary>
    public static NutritionalCareError? TryGuidelineCodes(IReadOnlyList<string>? codes,
        IReadOnlyList<string>? custom, out List<Guideline> result)
    {
        result = [];
        try
        {
            result.AddRange(NonBlank(codes).Select(Guideline.FromCode));
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.UnknownGuideline;
        }

        if (TryGuidelines([], custom, out var customGuidelines) is { } invalid) return invalid;
        result = result.Concat(customGuidelines).Distinct().ToList();
        return result.Count(g => g.IsCustom) > Guideline.MaximumCustomPerVersion
            ? NutritionalCareError.TooManyCustomGuidelines
            : null;
    }

    /// <summary>Business rule: Closed Restriction List. An unknown code is rejected, never stored as text.</summary>
    public static NutritionalCareError? TryRestrictions(IReadOnlyList<string>? restrictions,
        out List<DietaryRestriction> result)
    {
        result = [];
        try
        {
            result.AddRange(NonBlank(restrictions).Select(r => new DietaryRestriction(r)).Distinct());
            return null;
        }
        catch (ArgumentException)
        {
            return NutritionalCareError.UnknownRestriction;
        }
    }

    private static IEnumerable<string> NonBlank(IReadOnlyList<string>? values)
    {
        return (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v));
    }
}
