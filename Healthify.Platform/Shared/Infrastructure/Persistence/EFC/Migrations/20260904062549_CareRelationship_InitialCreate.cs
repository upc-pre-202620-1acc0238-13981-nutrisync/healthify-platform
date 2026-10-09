using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class CareRelationship_InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "care_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    patient_id = table.Column<int>(type: "int", nullable: false),
                    practitioner_id = table.Column<int>(type: "int", nullable: false),
                    established_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    discharged_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    discharge_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    pending_targets_version = table.Column<int>(type: "int", nullable: true),
                    last_acknowledged_version = table.Column<int>(type: "int", nullable: true),
                    consent_granted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    consent_scope = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    consent_granted_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    consent_withdrawn_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_care_links", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    issued_by = table.Column<int>(type: "int", nullable: false),
                    token = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    redeemed_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_invitations", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_care_links_patient_id",
                table: "care_links",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_links_practitioner_id",
                table: "care_links",
                column: "practitioner_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_issued_by",
                table: "invitations",
                column: "issued_by");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_token",
                table: "invitations",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_links");

            migrationBuilder.DropTable(
                name: "invitations");
        }
    }
}
