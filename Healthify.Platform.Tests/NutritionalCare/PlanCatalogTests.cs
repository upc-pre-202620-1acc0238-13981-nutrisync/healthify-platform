using Healthify.Platform.NutritionalCare.Application.Internal.CommandServices;
using Healthify.Platform.NutritionalCare.Domain.Model.Errors;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.Tests.TestSupport;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-6. The closed catalogs of EV-5: restrictions (no "Otra restricción"), guidelines as a code or a
///     custom text ("Otra indicación", 3 to 140 characters, at most 5 per version).
/// </summary>
public class PlanCatalogTests
{
    [Fact]
    public void Restrictions_are_closed_to_the_eight_codes_of_EV_5()
    {
        Assert.Equal(
            ["LactoseFree", "GlutenFree", "Vegan", "Vegetarian", "TreeNutFree", "ShellfishFree", "Kosher", "Halal"],
            DietaryRestriction.All);
        Assert.Equal("GlutenFree", new DietaryRestriction(" glutenfree ").Value);
        Assert.Throws<ArgumentException>(() => new DietaryRestriction("Sin cerdo"));
        Assert.Throws<ArgumentException>(() => new DietaryRestriction("Other"));
    }

    [Fact]
    public void The_guideline_catalog_includes_the_one_of_PR14_IA_A()
    {
        Assert.Equal(
        [
            "PrioritizeVegetables", "Drink2LWater", "AvoidSugaryDrinks", "ProteinAtBreakfast", "ReduceSalt",
            "EatEvery3To4Hours", "ProteinAndVegetablesAtDinner"
        ], Guideline.Codes);
        Assert.Equal("ReduceSalt", Guideline.FromCode("reducesalt").Code);
        Assert.Throws<ArgumentException>(() => Guideline.FromCode("Reduce la sal"));
    }

    [Theory]
    [InlineData("ab", false)]
    [InlineData("abc", true)]
    [InlineData("Caminar 20 min", true)]
    public void A_custom_guideline_has_between_3_and_140_characters(string text, bool valid)
    {
        if (valid) Assert.Equal(text, Guideline.CustomText(text).Custom);
        else Assert.Throws<ArgumentException>(() => Guideline.CustomText(text));

        Assert.Throws<ArgumentException>(() => Guideline.CustomText(new string('x', 141)));
        Assert.Equal(140, Guideline.CustomText(new string('x', 140)).Custom!.Length);
    }

    [Fact]
    public void On_the_stand_alone_endpoints_a_code_stays_a_code_and_any_other_text_is_custom()
    {
        var error = PlanCatalogInput.TryGuidelines(["ReduceSalt", "Prioriza vegetales"], ["Caminar 20 min"],
            out var guidelines);

        Assert.Null(error);
        Assert.Equal([Guideline.FromCode("ReduceSalt"), Guideline.CustomText("Prioriza vegetales"),
            Guideline.CustomText("Caminar 20 min")], guidelines);
    }

    [Fact]
    public void At_most_five_custom_guidelines_per_version()
    {
        var six = Enumerable.Range(1, 6).Select(i => $"Indicación {i}").ToList();

        Assert.Equal(NutritionalCareError.TooManyCustomGuidelines,
            PlanCatalogInput.TryGuidelines([], six, out _));
        Assert.Null(PlanCatalogInput.TryGuidelines(["ReduceSalt", "AvoidSugaryDrinks"], six.Take(5).ToList(), out _));

        var plan = PlanScenario.Prescribed(1, 10, 20, 2, 1);
        Assert.Throws<ArgumentException>(() => plan.PublishWith(six.Select(Guideline.CustomText), []));
    }

    [Fact]
    public void Each_input_failure_has_its_own_error()
    {
        Assert.Equal(NutritionalCareError.UnknownRestriction,
            PlanCatalogInput.TryRestrictions(["LactoseFree", "Sin cerdo"], out _));
        Assert.Equal(NutritionalCareError.InvalidCustomGuideline,
            PlanCatalogInput.TryGuidelines([], ["ok"], out _));
        Assert.Equal(NutritionalCareError.UnknownGuideline,
            PlanCatalogInput.TryGuidelineCodes(["ReduceSalt", "Caminar"], [], out _));
    }

    [Fact]
    public void Repeated_items_are_kept_once()
    {
        Assert.Null(PlanCatalogInput.TryRestrictions(["Vegan", "vegan"], out var restrictions));
        Assert.Single(restrictions);

        var plan = PlanScenario.Prescribed(1, 10, 20, 2, 1);
        plan.PublishWith([Guideline.FromCode("ReduceSalt"), Guideline.FromCode("ReduceSalt")],
            [new DietaryRestriction("Vegan")]);
        Assert.Single(plan.Guidelines);
        Assert.Equal(["Vegan"], plan.Restrictions);
    }
}
