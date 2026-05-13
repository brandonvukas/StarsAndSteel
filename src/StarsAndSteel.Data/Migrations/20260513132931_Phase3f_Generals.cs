using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StarsAndSteel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3f_Generals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Generals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameWorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AssignedProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    XpLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Generals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Generals_GameWorlds_GameWorldId",
                        column: x => x.GameWorldId,
                        principalTable: "GameWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Generals_Players_OwnerPlayerId",
                        column: x => x.OwnerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Generals_Provinces_AssignedProvinceId",
                        column: x => x.AssignedProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Generals_AssignedProvinceId",
                table: "Generals",
                column: "AssignedProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_Generals_GameWorldId_AssignedProvinceId",
                table: "Generals",
                columns: new[] { "GameWorldId", "AssignedProvinceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Generals_GameWorldId_OwnerPlayerId",
                table: "Generals",
                columns: new[] { "GameWorldId", "OwnerPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Generals_OwnerPlayerId",
                table: "Generals",
                column: "OwnerPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Generals");
        }
    }
}
