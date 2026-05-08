using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StarsAndSteel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConstructionOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConstructionOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameWorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    UnitType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    BuildingType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    IssuedAtTick = table.Column<int>(type: "int", nullable: false),
                    TicksRemaining = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConstructionOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConstructionOrders_GameWorlds_GameWorldId",
                        column: x => x.GameWorldId,
                        principalTable: "GameWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConstructionOrders_Players_OwnerPlayerId",
                        column: x => x.OwnerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConstructionOrders_Provinces_ProvinceId",
                        column: x => x.ProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionOrders_GameWorldId_Status_IssuedAtTick",
                table: "ConstructionOrders",
                columns: new[] { "GameWorldId", "Status", "IssuedAtTick" });

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionOrders_OwnerPlayerId",
                table: "ConstructionOrders",
                column: "OwnerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionOrders_ProvinceId",
                table: "ConstructionOrders",
                column: "ProvinceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConstructionOrders");
        }
    }
}
