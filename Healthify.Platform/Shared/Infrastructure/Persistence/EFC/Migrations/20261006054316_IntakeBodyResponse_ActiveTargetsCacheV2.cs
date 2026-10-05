using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <summary>
    ///     NC-6. The patient copy of the contract (<c>active_targets_caches</c>) gets the guidelines as objects
    ///     (<c>guideline_items</c>) and the legacy restrictions apart. <c>guidelines</c> keeps its list of strings,
    ///     the original contract. Data migration: every cached guideline becomes <c>{ "custom": … }</c>; the
    ///     restrictions follow the same rule as <c>NutritionalCare_GuidelineCodes</c> (code or legacy text).
///     The original restrictions are kept in the backup column <c>restrictions_before_nc6</c> (not mapped in the
///     model; audit and Down only).
    /// </summary>
    /// <remarks>NC-8 and NC-9 add their own columns to this table in a later migration.</remarks>
    public partial class IntakeBodyResponse_ActiveTargetsCacheV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable first, filled by the data step, then NOT NULL.
            migrationBuilder.AddColumn<string>(
                name: "guideline_items",
                table: "active_targets_caches",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "legacy_restrictions",
                table: "active_targets_caches",
                type: "json",
                nullable: true);

            migrationBuilder.Sql(NutritionalCare_GuidelineCodes.RaiseGroupConcatLimit);
            // The cached guidelines as objects, in their original order.
            migrationBuilder.Sql("UPDATE `active_targets_caches` t SET t.guideline_items = " +
                                 NutritionalCare_GuidelineCodes.GuidelinesAsCustom("t.guidelines") + ";");
            // The original restrictions are copied before they are converted (audit and an exact Down).
            NutritionalCare_GuidelineCodes.BackUpRestrictions(migrationBuilder, "active_targets_caches");
            migrationBuilder.Sql(NutritionalCare_GuidelineCodes.RestrictionsUpSql("active_targets_caches"));

            migrationBuilder.AlterColumn<string>(
                name: "guideline_items",
                table: "active_targets_caches",
                type: "json",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "json",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "legacy_restrictions",
                table: "active_targets_caches",
                type: "json",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "json",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(NutritionalCare_GuidelineCodes.RaiseGroupConcatLimit);
            migrationBuilder.Sql(NutritionalCare_GuidelineCodes.RestrictionsDownSql("active_targets_caches"));

            migrationBuilder.DropColumn(
                name: "guideline_items",
                table: "active_targets_caches");

            migrationBuilder.DropColumn(
                name: "legacy_restrictions",
                table: "active_targets_caches");

            NutritionalCare_GuidelineCodes.DropRestrictionsBackup(migrationBuilder, "active_targets_caches");
        }
    }
}
