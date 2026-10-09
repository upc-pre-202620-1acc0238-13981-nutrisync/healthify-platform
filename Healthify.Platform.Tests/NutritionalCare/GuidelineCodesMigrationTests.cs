using Healthify.Platform.IntakeBodyResponse.Domain.Model.Aggregates;
using Healthify.Platform.IntakeBodyResponse.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.NutritionalCare.Domain.Model.Aggregates;
using Healthify.Platform.NutritionalCare.Domain.Model.ValueObjects;
using Healthify.Platform.NutritionalCare.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Configuration;
using Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Healthify.Platform.Tests.NutritionalCare;

/// <summary>
///     NC-6. The data migration of the free text lists (NutritionalCare_GuidelineCodes and
///     IntakeBodyResponse_ActiveTargetsCacheV2) and the JSON mapping that reads both shapes.
/// </summary>
public class GuidelineCodesMigrationTests
{
    [Fact]
    public void Old_guidelines_become_custom_guidelines()
    {
        Assert.Equal([(null, "Prioriza vegetales"), (null, "Toma 2 L de agua al día")],
            NutritionalCare_GuidelineCodes.ConvertGuidelines(["Prioriza vegetales", " Toma 2 L de agua al día ", ""]));
    }

    [Fact]
    public void Known_restrictions_become_codes_and_the_rest_is_kept_as_legacy()
    {
        var (codes, legacy) = NutritionalCare_GuidelineCodes.ClassifyRestrictions(["Sin cerdo", "Sin lactosa"]);

        Assert.Equal(["LactoseFree"], codes);
        Assert.Equal(["Sin cerdo"], legacy);
    }

    [Theory]
    [InlineData("SIN GLÚTEN", "GlutenFree")]
    [InlineData("Vegana", "Vegan")]
    [InlineData("vegetarian", "Vegetarian")]
    [InlineData("Lactose-free", "LactoseFree")]
    [InlineData("  Sin mariscos ", "ShellfishFree")]
    [InlineData("ShellfishFree", "ShellfishFree")]
    [InlineData("Kosher", "Kosher")]
    public void Labels_match_ignoring_case_accents_and_spaces(string stored, string expected)
    {
        Assert.Equal([expected], NutritionalCare_GuidelineCodes.ClassifyRestrictions([stored]).Codes);
    }

    [Fact]
    public void Every_label_maps_to_a_code_of_the_closed_list_and_every_code_is_recognised()
    {
        Assert.All(NutritionalCare_GuidelineCodes.RestrictionLabels.Values,
            code => Assert.Contains(code, DietaryRestriction.All));
        Assert.All(DietaryRestriction.All,
            code => Assert.Equal([code], NutritionalCare_GuidelineCodes.ClassifyRestrictions([code]).Codes));
    }

    [Fact]
    public void Plan_guidelines_are_stored_as_objects_and_old_strings_are_read_as_custom()
    {
        var stored = GuidelineJsonConverter.Serialize([Guideline.FromCode("ReduceSalt"), Guideline.CustomText("Caminar 20 min")]);
        Assert.Equal("""[{"code":"ReduceSalt"},{"custom":"Caminar 20 min"}]""", stored);

        Assert.Equal([Guideline.FromCode("ReduceSalt"), Guideline.CustomText("Caminar 20 min")],
            GuidelineJsonConverter.Deserialize(stored));
        // The shape before NC-6, should a row escape the data migration.
        Assert.Equal([Guideline.CustomText("Prioriza vegetales")],
            GuidelineJsonConverter.Deserialize("""["Prioriza vegetales"]"""));
        Assert.Empty(GuidelineJsonConverter.Deserialize("[]"));
    }

    [Fact]
    public void Cached_guidelines_are_stored_as_objects_and_old_strings_are_read_as_custom()
    {
        var items = CachedGuidelineJson.Deserialize("""[{"code":"ReduceSalt"},{"custom":"Caminar 20 min"},"Viejo"]""");

        Assert.Equal(["ReduceSalt", "Caminar 20 min", "Viejo"], items.Select(i => i.ToString()));
        Assert.Equal("ReduceSalt", items[0].Code);
        Assert.Equal("Viejo", items[2].Custom);
        Assert.Equal("""[{"code":"ReduceSalt"},{"custom":"Caminar 20 min"},{"custom":"Viejo"}]""",
            CachedGuidelineJson.Serialize(items));
    }

    [Theory]
    [InlineData(typeof(NutritionalCare_GuidelineCodes), "nutrition_plans")]
    [InlineData(typeof(IntakeBodyResponse_ActiveTargetsCacheV2), "active_targets_caches")]
    public void The_original_restrictions_are_backed_up_before_converting_and_restored_before_dropping(
        Type migrationType, string table)
    {
        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        var up = migration.UpOperations.ToList();
        var down = migration.DownOperations.ToList();

        var addBackup = up.FindIndex(o =>
            o is SqlOperation s && s.Sql == $"ALTER TABLE `{table}` ADD `restrictions_before_nc6` json NULL;");
        var copy = up.FindIndex(o => o is SqlOperation s && s.Sql.Contains("restrictions_before_nc6 = t.restrictions"));
        var convert = up.FindIndex(o => o is SqlOperation s && s.Sql.Contains("t.legacy_restrictions ="));
        Assert.True(addBackup >= 0 && addBackup < copy && copy < convert);

        var restore = down.FindIndex(o => o is SqlOperation s && s.Sql.Contains("THEN t.restrictions_before_nc6"));
        var dropBackup = down.FindIndex(o =>
            o is SqlOperation s && s.Sql == $"ALTER TABLE `{table}` DROP COLUMN `restrictions_before_nc6`;");
        Assert.True(restore >= 0 && restore < dropBackup);
    }

    [Fact]
    public void The_provider_generates_the_up_and_down_scripts_of_the_three_migrations()
    {
        // Generating the script needs the provider and the target models, not a server. This is where
        // MySql.EntityFrameworkCore threw when the backup column, absent from the model, went through AddColumn.
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("server=localhost;database=model_only;user=none;password=none").Options);
        var migrator = context.GetService<IMigrator>();

        var up = migrator.GenerateScript("20261005165442_NutritionalCare_AddConsultations",
            "20261006054316_IntakeBodyResponse_ActiveTargetsCacheV2");
        var down = migrator.GenerateScript("20261006054316_IntakeBodyResponse_ActiveTargetsCacheV2",
            "20261005165442_NutritionalCare_AddConsultations");

        Assert.Contains("ALTER TABLE `nutrition_plans` ADD `restrictions_before_nc6` json NULL;", up);
        Assert.Contains("ALTER TABLE `active_targets_caches` ADD `restrictions_before_nc6` json NULL;", up);
        Assert.Contains("GROUP_CONCAT(", up);
        Assert.Contains("ORDER BY jt.ord", up);
        Assert.Contains("ALTER TABLE `nutrition_plans` DROP COLUMN `restrictions_before_nc6`;", down);
        Assert.Contains("ALTER TABLE `active_targets_caches` DROP COLUMN `restrictions_before_nc6`;", down);
    }

    [Fact]
    public void The_restrictions_backup_is_not_part_of_the_model()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("server=localhost;database=model_only;user=none;password=none").Options);

        Assert.All(context.Model.GetEntityTypes().SelectMany(e => e.GetProperties()),
            p => Assert.NotEqual("restrictions_before_nc6", p.GetColumnName()));
    }

    [Fact]
    public void The_model_maps_the_new_json_columns()
    {
        // Building the model needs the provider, not a server: nothing connects.
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySQL("server=localhost;database=model_only;user=none;password=none").Options);

        var plan = context.Model.FindEntityType(typeof(NutritionPlan))!;
        Assert.Equal("json", plan.FindProperty("_guidelines")!.GetColumnType());
        Assert.Equal("legacy_restrictions", plan.FindProperty("_legacyRestrictions")!.GetColumnName());
        Assert.False(plan.FindProperty("_legacyRestrictions")!.IsNullable);
        Assert.True(plan.FindProperty("_droppedLegacyRestrictions")!.IsNullable);

        var cache = context.Model.FindEntityType(typeof(ActiveTargetsCache))!;
        Assert.Equal("guideline_items", cache.FindProperty("_guidelineItems")!.GetColumnName());
        Assert.Equal("legacy_restrictions", cache.FindProperty("_legacyRestrictions")!.GetColumnName());

        var converter = plan.FindProperty("_guidelines")!.GetValueConverter()!;
        var read = Assert.IsType<List<Guideline>>(converter.ConvertFromProvider("""["Prioriza vegetales"]"""));
        Assert.True(Assert.Single(read).IsCustom);
    }
}
