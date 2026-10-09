using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class IntakeBodyResponse_SimplifyWeighInProtocol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IN-3. Only «¿Te pesaste en ayunas?» is asked now. Existing answers are kept as declared;
            // new readings leave these two columns NULL. Trends are rebuilt by the one-shot job
            // (recalculate-weight-trends), never here.
            migrationBuilder.AlterColumn<bool>(
                name: "protocol_same_time_of_day",
                table: "self_weigh_ins",
                type: "tinyint(1)",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");

            migrationBuilder.AlterColumn<bool>(
                name: "protocol_same_scale",
                table: "self_weigh_ins",
                type: "tinyint(1)",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // IN-3. Readings recorded after IN-3 never answered these two questions. Before NOT NULL comes
            // back they are stored as false, which is how the three-condition protocol treated a missing
            // answer: those readings stop smoothing the trend under the old code. Lossy by necessity.
            migrationBuilder.Sql(
                "UPDATE self_weigh_ins SET protocol_same_time_of_day = 0 WHERE protocol_same_time_of_day IS NULL;");
            migrationBuilder.Sql(
                "UPDATE self_weigh_ins SET protocol_same_scale = 0 WHERE protocol_same_scale IS NULL;");

            migrationBuilder.AlterColumn<bool>(
                name: "protocol_same_time_of_day",
                table: "self_weigh_ins",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "protocol_same_scale",
                table: "self_weigh_ins",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldNullable: true);
        }
    }
}
