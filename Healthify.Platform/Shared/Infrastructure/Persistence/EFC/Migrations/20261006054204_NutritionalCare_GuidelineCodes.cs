using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <summary>
    ///     NC-6. Guidelines and restrictions of <c>nutrition_plans</c> become closed catalogs. Data migration:
    ///     every stored guideline string becomes <c>{ "custom": "&lt;text&gt;" }</c>; every stored restriction that is
    ///     a code or a known label (es/en) becomes its code, and the ones that match nothing move, as written, to
    ///     <c>legacy_restrictions</c>, so nothing clinical is lost and the patient keeps seeing them.
    ///     The original restrictions are kept, as written and in order, in the backup column
    ///     <c>restrictions_before_nc6</c> (not mapped in the model), so the Down restores them exactly.
    /// </summary>
    /// <remarks>
    ///     The label table is frozen here on purpose: a migration must produce the same data forever, whatever
    ///     the domain says later. <see cref="ClassifyRestrictions" /> and <see cref="ConvertGuidelines" /> are the
    ///     same rules in C#, for the tests; the SQL below is generated from the same table.
    /// </remarks>
    public partial class NutritionalCare_GuidelineCodes : Migration
    {
        /// <summary>Normalized label (lower case, no accents, trimmed) → restriction code.</summary>
        public static readonly IReadOnlyDictionary<string, string> RestrictionLabels = new Dictionary<string, string>
        {
            ["lactosefree"] = "LactoseFree", ["sin lactosa"] = "LactoseFree", ["libre de lactosa"] = "LactoseFree",
            ["lactose free"] = "LactoseFree", ["lactose-free"] = "LactoseFree",
            ["glutenfree"] = "GlutenFree", ["sin gluten"] = "GlutenFree", ["libre de gluten"] = "GlutenFree",
            ["gluten free"] = "GlutenFree", ["gluten-free"] = "GlutenFree",
            ["vegan"] = "Vegan", ["vegano"] = "Vegan", ["vegana"] = "Vegan",
            ["vegetarian"] = "Vegetarian", ["vegetariano"] = "Vegetarian", ["vegetariana"] = "Vegetarian",
            ["treenutfree"] = "TreeNutFree", ["sin frutos secos"] = "TreeNutFree", ["tree nut free"] = "TreeNutFree",
            ["tree-nut free"] = "TreeNutFree", ["tree-nut-free"] = "TreeNutFree",
            ["shellfishfree"] = "ShellfishFree", ["sin mariscos"] = "ShellfishFree", ["sin marisco"] = "ShellfishFree",
            ["shellfish free"] = "ShellfishFree", ["shellfish-free"] = "ShellfishFree",
            ["kosher"] = "Kosher",
            ["halal"] = "Halal"
        };

        /// <summary>The restriction rule of the data migration, in C#: (codes, legacy texts as written).</summary>
        public static (List<string> Codes, List<string> Legacy) ClassifyRestrictions(IEnumerable<string> stored)
        {
            var codes = new List<string>();
            var legacy = new List<string>();
            foreach (var raw in stored)
            {
                var text = raw?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                if (RestrictionLabels.TryGetValue(Normalize(text), out var code)) codes.Add(code);
                else legacy.Add(text);
            }

            return (codes, legacy);
        }

        /// <summary>The guideline rule of the data migration, in C#: every string becomes a custom guideline.</summary>
        public static List<(string Code, string Custom)> ConvertGuidelines(IEnumerable<string> stored)
        {
            return stored.Select(g => g?.Trim()).Where(g => !string.IsNullOrEmpty(g))
                .Select(g => ((string)null, g)).ToList();
        }

        /// <summary>Lower case, without accents, trimmed: what utf8mb4_0900_ai_ci compares in MySQL.</summary>
        private static string Normalize(string text)
        {
            var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    builder.Append(c);
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        ///     GROUP_CONCAT truncates at 1024 bytes by default; the ordered arrays below are built with it, so the
        ///     session limit is raised first. Migrations run on one connection, so this covers every step.
        /// </summary>
        internal const string RaiseGroupConcatLimit = "SET SESSION group_concat_max_len = 16777216;";

        /// <summary>SQL CASE mapping the restriction text <paramref name="text" /> to its code, or NULL.</summary>
        private static string RestrictionCase(string text)
        {
            var whens = string.Join(" ", RestrictionLabels.Select(l => $"WHEN '{l.Key}' THEN '{l.Value}'"));
            return $"(CASE LOWER(TRIM({text})) COLLATE utf8mb4_0900_ai_ci {whens} ELSE NULL END)";
        }

        /// <summary>
        ///     A JSON array of <paramref name="element" /> (JSON text) over the rows of <paramref name="rows" />, in
        ///     the order of their ordinality <c>jt.ord</c>: the original order of the list. JSON_ARRAYAGG cannot
        ///     take an ORDER BY in MySQL. An empty selection gives <c>[]</c>.
        /// </summary>
        internal static string OrderedArray(string element, string rows, string where)
        {
            return $"COALESCE((SELECT CAST(CONCAT('[', GROUP_CONCAT({element} ORDER BY jt.ord SEPARATOR ','), ']') " +
                   $"AS JSON) FROM {rows} WHERE {where}), JSON_ARRAY())";
        }

        /// <summary>
        ///     Each element of the JSON list <paramref name="column" /> as a row: <c>jt.ord</c> (1-based position) and
        ///     <c>jt.v</c> (the element as JSON, so no text is ever truncated by a column length).
        /// </summary>
        internal static string Elements(string column)
        {
            return $"JSON_TABLE({column}, '$[*]' COLUMNS (ord FOR ORDINALITY, v JSON PATH '$')) jt";
        }

        /// <summary>
        ///     The restriction data step, shared with <c>IntakeBodyResponse_ActiveTargetsCacheV2</c> (same rule,
        ///     other table). Expects <c>restrictions json</c> and a nullable <c>legacy_restrictions json</c>.
        /// </summary>
        internal static string RestrictionsUpSql(string table)
        {
            const string text = "JSON_UNQUOTE(jt.v)";
            var map = RestrictionCase(text);
            var rows = Elements("t.restrictions");
            const string isString = "JSON_TYPE(jt.v) = 'STRING'";
            return
                // Legacy first: MySQL evaluates single-table UPDATE assignments left to right, so this one still
                // reads the original restrictions.
                $"UPDATE `{table}` t SET " +
                $"t.legacy_restrictions = {OrderedArray($"JSON_QUOTE(TRIM({text}))", rows,
                    $"{isString} AND TRIM({text}) <> '' AND {map} IS NULL")}, " +
                $"t.restrictions = {OrderedArray($"JSON_QUOTE({map})", rows, $"{isString} AND {map} IS NOT NULL")};";
        }

        /// <summary>Guidelines in place, in their order: only rows still in the original shape (strings).</summary>
        private static string GuidelinesUpSql(string table)
        {
            return
                $"UPDATE `{table}` t SET " +
                $"t.guidelines = {GuidelinesAsCustom("t.guidelines")} " +
                "WHERE JSON_LENGTH(t.guidelines) > 0 AND JSON_TYPE(JSON_EXTRACT(t.guidelines, '$[0]')) = 'STRING';";
        }

        /// <summary>The list of strings <paramref name="column" /> as <c>[{ "custom": … }]</c>, in its order.</summary>
        internal static string GuidelinesAsCustom(string column)
        {
            return OrderedArray("CAST(JSON_OBJECT('custom', TRIM(JSON_UNQUOTE(jt.v))) AS CHAR)", Elements(column),
                "JSON_TYPE(jt.v) = 'STRING' AND TRIM(JSON_UNQUOTE(jt.v)) <> ''");
        }

        /// <summary>
        ///     Migration backup column: the restrictions exactly as they were stored before NC-6 (text and order).
        ///     It exists only for audit and for <see cref="RestrictionsDownSql" />: it is deliberately not mapped in
        ///     the EF model, the domain or any resource. Rows written after this migration leave it NULL.
        /// </summary>
        internal const string RestrictionsBackupColumn = "restrictions_before_nc6";

        /// <summary>Adds the backup column and copies the original restrictions into it, before any conversion.</summary>
        /// <remarks>
        ///     Raw SQL on purpose: MySql.EntityFrameworkCore looks every AddColumn/DropColumn up in the target model
        ///     to pick its character set, and this column is deliberately not in the model (it throws a
        ///     NullReferenceException while generating the SQL otherwise).
        /// </remarks>
        internal static void BackUpRestrictions(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.Sql($"ALTER TABLE `{table}` ADD `{RestrictionsBackupColumn}` json NULL;");
            migrationBuilder.Sql($"UPDATE `{table}` t SET t.{RestrictionsBackupColumn} = t.restrictions;");
        }

        /// <summary>
        ///     Back to one list of strings. Rows migrated by the Up get their original list back from the backup
        ///     column, exactly. Rows written afterwards (backup NULL) get the codes, then the legacy texts: there was
        ///     no original text for them.
        /// </summary>
        internal static string RestrictionsDownSql(string table)
        {
            return $"UPDATE `{table}` t SET t.restrictions = CASE " +
                   $"WHEN t.{RestrictionsBackupColumn} IS NOT NULL THEN t.{RestrictionsBackupColumn} " +
                   "ELSE JSON_MERGE_PRESERVE(t.restrictions, COALESCE(t.legacy_restrictions, JSON_ARRAY())) END;";
        }

        /// <summary>Drops the backup column once the Down has read it. Raw SQL, like <see cref="BackUpRestrictions" />.</summary>
        internal static void DropRestrictionsBackup(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.Sql($"ALTER TABLE `{table}` DROP COLUMN `{RestrictionsBackupColumn}`;");
        }

        /// <summary>Guidelines back to strings, in their order: the code or the custom text.</summary>
        private static string GuidelinesDownSql(string table)
        {
            var rows = "JSON_TABLE(t.guidelines, '$[*]' COLUMNS (ord FOR ORDINALITY, " +
                       "c JSON PATH '$.code', u JSON PATH '$.custom')) jt";
            return
                $"UPDATE `{table}` t SET " +
                $"t.guidelines = {OrderedArray("CAST(COALESCE(jt.c, jt.u) AS CHAR)", rows,
                    "COALESCE(jt.c, jt.u) IS NOT NULL")} " +
                "WHERE JSON_LENGTH(t.guidelines) > 0 AND JSON_TYPE(JSON_EXTRACT(t.guidelines, '$[0]')) = 'OBJECT';";
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RaiseGroupConcatLimit);

            migrationBuilder.AddColumn<string>(
                name: "dropped_legacy_restrictions",
                table: "nutrition_plans",
                type: "json",
                nullable: true);

            // Nullable first, filled by the data step, then NOT NULL: no row is ever left without a value.
            migrationBuilder.AddColumn<string>(
                name: "legacy_restrictions",
                table: "nutrition_plans",
                type: "json",
                nullable: true);

            // The original restrictions are copied before they are converted (audit and an exact Down).
            BackUpRestrictions(migrationBuilder, "nutrition_plans");
            migrationBuilder.Sql(RestrictionsUpSql("nutrition_plans"));
            migrationBuilder.Sql(GuidelinesUpSql("nutrition_plans"));

            migrationBuilder.AlterColumn<string>(
                name: "legacy_restrictions",
                table: "nutrition_plans",
                type: "json",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "json",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RaiseGroupConcatLimit);
            migrationBuilder.Sql(RestrictionsDownSql("nutrition_plans"));
            migrationBuilder.Sql(GuidelinesDownSql("nutrition_plans"));

            migrationBuilder.DropColumn(
                name: "dropped_legacy_restrictions",
                table: "nutrition_plans");

            migrationBuilder.DropColumn(
                name: "legacy_restrictions",
                table: "nutrition_plans");

            DropRestrictionsBackup(migrationBuilder, "nutrition_plans");
        }
    }
}
