using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StarsAndSteel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3d_CyberAttackOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CyberAttackOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameWorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttackerPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaunchProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectKind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    IssuedAtTick = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CyberAttackOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CyberAttackOrders_GameWorlds_GameWorldId",
                        column: x => x.GameWorldId,
                        principalTable: "GameWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CyberAttackOrders_Players_AttackerPlayerId",
                        column: x => x.AttackerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CyberAttackOrders_Provinces_LaunchProvinceId",
                        column: x => x.LaunchProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CyberAttackOrders_Provinces_TargetProvinceId",
                        column: x => x.TargetProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CyberAttackOrders_AttackerPlayerId",
                table: "CyberAttackOrders",
                column: "AttackerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_CyberAttackOrders_GameWorldId_Status_IssuedAtTick",
                table: "CyberAttackOrders",
                columns: new[] { "GameWorldId", "Status", "IssuedAtTick" });

            migrationBuilder.CreateIndex(
                name: "IX_CyberAttackOrders_LaunchProvinceId",
                table: "CyberAttackOrders",
                column: "LaunchProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_CyberAttackOrders_TargetProvinceId",
                table: "CyberAttackOrders",
                column: "TargetProvinceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CyberAttackOrders");
        }
    }
}
