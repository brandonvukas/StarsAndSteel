using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StarsAndSteel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTreatyOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TreatyOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameWorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiverPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProposedAtTick = table.Column<int>(type: "int", nullable: false),
                    ExpiresAtTick = table.Column<int>(type: "int", nullable: false),
                    ResolvedAtTick = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatyOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreatyOffers_GameWorlds_GameWorldId",
                        column: x => x.GameWorldId,
                        principalTable: "GameWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TreatyOffers_Players_ReceiverPlayerId",
                        column: x => x.ReceiverPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TreatyOffers_Players_SenderPlayerId",
                        column: x => x.SenderPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreatyOffers_GameWorldId_ReceiverPlayerId_Status",
                table: "TreatyOffers",
                columns: new[] { "GameWorldId", "ReceiverPlayerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatyOffers_GameWorldId_Status_ExpiresAtTick",
                table: "TreatyOffers",
                columns: new[] { "GameWorldId", "Status", "ExpiresAtTick" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatyOffers_ReceiverPlayerId",
                table: "TreatyOffers",
                column: "ReceiverPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatyOffers_SenderPlayerId",
                table: "TreatyOffers",
                column: "SenderPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TreatyOffers");
        }
    }
}
