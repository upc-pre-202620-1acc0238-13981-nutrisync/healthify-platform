using System.Text.Json;
using Healthify.Platform.Shared.Application.Ai;
using Healthify.Platform.Shared.Infrastructure.Ai.Prompts;

namespace Healthify.Platform.Tests.Ai;

/// <summary>IA-0. The schema check of guard 6 and the versioned prompt catalog.</summary>
public class AiSchemaAndPromptCatalogTests
{
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "kcal": { "type": "integer", "minimum": 0, "maximum": 1200 },
            "tone": { "type": "string", "enum": ["invitation", "neutral"] },
            "tags": { "type": "array", "items": { "type": "string", "maxLength": 5 }, "minItems": 1 },
            "note": { "type": ["string", "null"] }
          },
          "required": ["kcal", "tone"],
          "additionalProperties": false
        }
        """;

    [Theory]
    [InlineData("""{"kcal":500,"tone":"neutral","tags":["a"],"note":null}""", 0)]
    [InlineData("""{"kcal":500,"tone":"neutral"}""", 0)]
    [InlineData("""{"kcal":1500,"tone":"neutral"}""", 1)] // above maximum
    [InlineData("""{"kcal":5.5,"tone":"neutral"}""", 1)] // not an integer
    [InlineData("""{"kcal":500,"tone":"blame"}""", 1)] // not in enum
    [InlineData("""{"kcal":500}""", 1)] // required
    [InlineData("""{"kcal":500,"tone":"neutral","extra":1}""", 1)] // closed object
    [InlineData("""{"kcal":500,"tone":"neutral","tags":[]}""", 1)] // minItems
    [InlineData("""{"kcal":500,"tone":"neutral","tags":["toolong"]}""", 1)] // maxLength
    [InlineData("""[1]""", 1)] // not an object
    public void The_schema_check_reports_each_violation(string instance, int violations)
    {
        using var value = JsonDocument.Parse(instance);
        using var schema = JsonDocument.Parse(Schema);

        Assert.Equal(violations, AiJsonSchemaValidator.Validate(value.RootElement, schema.RootElement).Count);
    }

    [Fact]
    public void The_catalog_serves_the_latest_version_without_its_schema_block()
    {
        var catalog = new PromptCatalog([
            File("meal-ideas@1.md", "v1"),
            File("meal-ideas@10.md", "v10"),
            File("meal-ideas@2.md", "v2"),
            new("README.md", "not a prompt")
        ]);

        var prompt = catalog.FindLatest(AiFeature.MealIdeas)!;

        Assert.Equal("meal-ideas@10", prompt.Version);
        Assert.Contains("Version v10", prompt.Text);
        Assert.DoesNotContain("```", prompt.Text);
        Assert.Equal("""{"type":"object"}""", prompt.OutputSchemaJson);
        Assert.Equal("Version v10. Answer in en.", prompt.Render("en").Split('\n')[0]);
        Assert.Null(catalog.FindLatest(AiFeature.WeeklySummary));
    }

    [Fact]
    public void A_prompt_without_an_output_schema_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PromptCatalog([new KeyValuePair<string, string>("weekly-summary@1.md", "# Role\nNo schema.")]));
    }

    [Fact]
    public void Every_prompt_embedded_in_the_platform_parses()
    {
        // Today no function has a prompt yet; the day one lands, a broken file fails here, not at startup.
        var catalog = PromptCatalog.FromEmbeddedResources(typeof(Program).Assembly);

        foreach (var feature in AiFeature.All)
            if (catalog.FindLatest(feature) is { } prompt)
                Assert.StartsWith(feature.PromptName + "@", prompt.Version);
    }

    private static KeyValuePair<string, string> File(string name, string version)
    {
        return new KeyValuePair<string, string>(name,
            $"Version {version}. Answer in {{{{language}}}}.\n\nRules.\n\n## Output schema\n\n```json\n{{\"type\":\"object\"}}\n```\n");
    }
}
