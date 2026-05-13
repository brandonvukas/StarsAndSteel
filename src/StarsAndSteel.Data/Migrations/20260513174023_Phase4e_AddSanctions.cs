using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StarsAndSteel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4e_AddSanctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSanctioning",
                table: "DiplomaticRelations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSanctioning",
                table: "DiplomaticRelations");
        }
    }
}
