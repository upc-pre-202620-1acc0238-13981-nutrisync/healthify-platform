using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Infrastructure.Calculators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>NC-3. Activity factors: defaults in code, overridable in NutritionalCare:ActivityFactors.</summary>
public class ActivityFactorProviderTests
{
    [Fact]
    public void Without_configuration_the_defaults_in_code_apply()
    {
        var provider = Provider(new Dictionary<string, string?>());

        Assert.Equal(1.2m, provider.FactorFor(new ActivityLevel(ActivityLevel.Sedentary)));
        Assert.Equal(1.375m, provider.FactorFor(new ActivityLevel(ActivityLevel.Light)));
        Assert.Equal(1.55m, provider.FactorFor(new ActivityLevel(ActivityLevel.Moderate)));
        Assert.Equal(1.725m, provider.FactorFor(new ActivityLevel(ActivityLevel.Intense)));
    }

    [Fact]
    public void A_configured_factor_overrides_the_default()
    {
        var provider = Provider(new Dictionary<string, string?>
        {
            ["NutritionalCare:ActivityFactors:Moderate"] = "1.6"
        });

        Assert.Equal(1.6m, provider.FactorFor(new ActivityLevel(ActivityLevel.Moderate)));
        Assert.Equal(1.2m, provider.FactorFor(new ActivityLevel(ActivityLevel.Sedentary)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0.9")]
    [InlineData("3")]
    public void An_empty_malformed_or_implausible_factor_falls_back_to_the_default(string configured)
    {
        var provider = Provider(new Dictionary<string, string?>
        {
            ["NutritionalCare:ActivityFactors:Intense"] = configured
        });

        Assert.Equal(1.725m, provider.FactorFor(new ActivityLevel(ActivityLevel.Intense)));
    }

    private static ConfiguredActivityFactorProvider Provider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ConfiguredActivityFactorProvider(configuration,
            NullLogger<ConfiguredActivityFactorProvider>.Instance);
    }
}
