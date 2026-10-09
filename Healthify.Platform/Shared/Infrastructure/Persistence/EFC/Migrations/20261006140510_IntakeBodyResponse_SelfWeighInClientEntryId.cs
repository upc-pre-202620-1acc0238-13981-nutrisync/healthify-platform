using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Healthify.Platform.Shared.Infrastructure.Persistence.EFC.Migrations
{
    /// <inheritdoc />
    public partial class IntakeBodyResponse_SelfWeighInClientEntryId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "client_entry_id",
                table: "self_weigh_ins",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_self_weigh_ins_patient_id_client_entry_id",
                table: "self_weigh_ins",
                columns: new[] { "patient_id", "client_entry_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_self_weigh_ins_patient_id_client_entry_id",
                table: "self_weigh_ins");

            migrationBuilder.DropColumn(
                name: "client_entry_id",
                table: "self_weigh_ins");
        }
    }
}
