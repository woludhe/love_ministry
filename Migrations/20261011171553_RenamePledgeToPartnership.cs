using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class RenamePledgeToPartnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // GivingType is stored as its enum name
            migrationBuilder.Sql("UPDATE Givings SET GivingType = 'Partnership' WHERE GivingType = 'Pledge';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Givings SET GivingType = 'Pledge' WHERE GivingType = 'Partnership';");
        }
    }
}
