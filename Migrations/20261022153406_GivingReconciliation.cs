using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class GivingReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PaymentStatus",
                table: "Givings",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsManualPayment",
                table: "Givings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MethodCode",
                table: "Givings",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReceivedReference",
                table: "Givings",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReconciledAt",
                table: "Givings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReconciledBy",
                table: "Givings",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReconciliationNote",
                table: "Givings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // Older gifts didn't record their method; ones a member confirmed via "I have paid" were manual
            migrationBuilder.Sql(
                "UPDATE Givings SET IsManualPayment = 1 " +
                "WHERE PaymentStatus = 'PendingVerification' OR Notes LIKE '%[Member confirmed payment]%';");

            migrationBuilder.CreateIndex(
                name: "IX_Givings_IsManualPayment_PaymentStatus",
                table: "Givings",
                columns: new[] { "IsManualPayment", "PaymentStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Givings_IsManualPayment_PaymentStatus",
                table: "Givings");

            migrationBuilder.DropColumn(
                name: "IsManualPayment",
                table: "Givings");

            migrationBuilder.DropColumn(
                name: "MethodCode",
                table: "Givings");

            migrationBuilder.DropColumn(
                name: "ReceivedReference",
                table: "Givings");

            migrationBuilder.DropColumn(
                name: "ReconciledAt",
                table: "Givings");

            migrationBuilder.DropColumn(
                name: "ReconciledBy",
                table: "Givings");

            migrationBuilder.DropColumn(
                name: "ReconciliationNote",
                table: "Givings");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentStatus",
                table: "Givings",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
