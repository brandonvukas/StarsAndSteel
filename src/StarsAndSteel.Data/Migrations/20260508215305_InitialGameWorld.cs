using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StarsAndSteel.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialGameWorld : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameWorlds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CurrentTick = table.Column<int>(type: "int", nullable: false),
                    TickIntervalSeconds = table.Column<int>(type: "int", nullable: false, defaultValue: 60),
                    NextTickDueUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MapSeed = table.Column<int>(type: "int", nullable: false),
                    RngState = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameWorlds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GameWorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsAi = table.Column<bool>(type: "bit", nullable: false),
                    AiPersonality = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NationName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    FlagPrimaryHex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    FlagSecondaryHex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    IsAlive = table.Column<bool>(type: "bit", nullable: false),
                    Money = table.Column<long>(type: "bigint", nullable: false),
                    Oil = table.Column<long>(type: "bigint", nullable: false),
                    Steel = table.Column<long>(type: "bigint", nullable: false),
                    Electronics = table.Column<long>(type: "bigint", nullable: false),
                    Food = table.Column<long>(type: "bigint", nullable: false),
                    Manpower = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Players_GameWorlds_GameWorldId",
                        column: x => x.GameWorldId,
                        principalTable: "GameWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiMemories",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemoryJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiMemories", x => x.PlayerId);
                    table.ForeignKey(
                        name: "FK_AiMemories_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameWorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Body = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_GameWorlds_GameWorldId",
                        column: x => x.GameWorldId,
                        principalTable: "GameWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Players_FromPlayerId",
                        column: x => x.FromPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChatMessages_Players_ToPlayerId",
                        column: x => x.ToPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DiplomaticRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameWorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TrustScore = table.Column<int>(type: "int", nullable: false),
                    LastChangedAtTick = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiplomaticRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiplomaticRelations_GameWorlds_GameWorldId",
                        column: x => x.GameWorldId,
                        principalTable: "GameWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiplomaticRelations_Players_FromPlayerId",
                        column: x => x.FromPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DiplomaticRelations_Players_ToPlayerId",
                        column: x => x.ToPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NewsItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameWorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tick = table.Column<int>(type: "int", nullable: false),
                    Headline = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RelatedPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsItems_GameWorlds_GameWorldId",
                        column: x => x.GameWorldId,
                        principalTable: "GameWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NewsItems_Players_RelatedPlayerId",
                        column: x => x.RelatedPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Provinces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameWorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OwnerPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsCoastal = table.Column<bool>(type: "bit", nullable: false),
                    CenterX = table.Column<float>(type: "real", nullable: false),
                    CenterY = table.Column<float>(type: "real", nullable: false),
                    MoraleLevel = table.Column<int>(type: "int", nullable: false),
                    BasePopulation = table.Column<int>(type: "int", nullable: false),
                    MoneyPerTick = table.Column<int>(type: "int", nullable: false),
                    OilPerTick = table.Column<int>(type: "int", nullable: false),
                    SteelPerTick = table.Column<int>(type: "int", nullable: false),
                    ElectronicsPerTick = table.Column<int>(type: "int", nullable: false),
                    FoodPerTick = table.Column<int>(type: "int", nullable: false),
                    ManpowerPerTick = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Provinces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Provinces_GameWorlds_GameWorldId",
                        column: x => x.GameWorldId,
                        principalTable: "GameWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Provinces_Players_OwnerPlayerId",
                        column: x => x.OwnerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ResearchProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TechId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProgressPoints = table.Column<int>(type: "int", nullable: false),
                    IsUnlocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchProgress_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Buildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(28)", maxLength: 28, nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    ConstructedAtTick = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buildings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Buildings_Provinces_ProvinceId",
                        column: x => x.ProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProvinceAdjacencies",
                columns: table => new
                {
                    ProvinceAId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProvinceBId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TerrainCost = table.Column<float>(type: "real", nullable: false, defaultValue: 1f),
                    IsSeaCrossing = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvinceAdjacencies", x => new { x.ProvinceAId, x.ProvinceBId });
                    table.ForeignKey(
                        name: "FK_ProvinceAdjacencies_Provinces_ProvinceAId",
                        column: x => x.ProvinceAId,
                        principalTable: "Provinces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProvinceAdjacencies_Provinces_ProvinceBId",
                        column: x => x.ProvinceBId,
                        principalTable: "Provinces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameWorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Strength = table.Column<int>(type: "int", nullable: false),
                    Morale = table.Column<int>(type: "int", nullable: false),
                    Experience = table.Column<int>(type: "int", nullable: false),
                    IsInTransit = table.Column<bool>(type: "bit", nullable: false),
                    TransitFromProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransitToProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransitArrivalTick = table.Column<int>(type: "int", nullable: true),
                    HomeBaseProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Units_GameWorlds_GameWorldId",
                        column: x => x.GameWorldId,
                        principalTable: "GameWorlds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Units_Players_OwnerPlayerId",
                        column: x => x.OwnerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Units_Provinces_HomeBaseProvinceId",
                        column: x => x.HomeBaseProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Units_Provinces_LocationProvinceId",
                        column: x => x.LocationProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Units_Provinces_TransitFromProvinceId",
                        column: x => x.TransitFromProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Units_Provinces_TransitToProvinceId",
                        column: x => x.TransitToProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UnitOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TargetProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IssuedAtTick = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitOrders_Provinces_TargetProvinceId",
                        column: x => x.TargetProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UnitOrders_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_ProvinceId",
                table: "Buildings",
                column: "ProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_FromPlayerId",
                table: "ChatMessages",
                column: "FromPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_GameWorldId_SentAtUtc",
                table: "ChatMessages",
                columns: new[] { "GameWorldId", "SentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ToPlayerId",
                table: "ChatMessages",
                column: "ToPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_DiplomaticRelations_FromPlayerId",
                table: "DiplomaticRelations",
                column: "FromPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_DiplomaticRelations_GameWorldId_FromPlayerId",
                table: "DiplomaticRelations",
                columns: new[] { "GameWorldId", "FromPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_DiplomaticRelations_ToPlayerId",
                table: "DiplomaticRelations",
                column: "ToPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_GameWorldId_Tick",
                table: "NewsItems",
                columns: new[] { "GameWorldId", "Tick" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_RelatedPlayerId",
                table: "NewsItems",
                column: "RelatedPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_GameWorldId_IsAi",
                table: "Players",
                columns: new[] { "GameWorldId", "IsAi" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_UserId",
                table: "Players",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvinceAdjacencies_ProvinceBId",
                table: "ProvinceAdjacencies",
                column: "ProvinceBId");

            migrationBuilder.CreateIndex(
                name: "IX_Provinces_GameWorldId",
                table: "Provinces",
                column: "GameWorldId");

            migrationBuilder.CreateIndex(
                name: "IX_Provinces_GameWorldId_OwnerPlayerId",
                table: "Provinces",
                columns: new[] { "GameWorldId", "OwnerPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Provinces_OwnerPlayerId",
                table: "Provinces",
                column: "OwnerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchProgress_PlayerId_TechId",
                table: "ResearchProgress",
                columns: new[] { "PlayerId", "TechId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitOrders_Status_IssuedAtTick",
                table: "UnitOrders",
                columns: new[] { "Status", "IssuedAtTick" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitOrders_TargetProvinceId",
                table: "UnitOrders",
                column: "TargetProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitOrders_UnitId",
                table: "UnitOrders",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Units_Domain",
                table: "Units",
                column: "Domain");

            migrationBuilder.CreateIndex(
                name: "IX_Units_GameWorldId_OwnerPlayerId",
                table: "Units",
                columns: new[] { "GameWorldId", "OwnerPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Units_HomeBaseProvinceId",
                table: "Units",
                column: "HomeBaseProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_Units_LocationProvinceId",
                table: "Units",
                column: "LocationProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_Units_OwnerPlayerId",
                table: "Units",
                column: "OwnerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Units_TransitFromProvinceId",
                table: "Units",
                column: "TransitFromProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_Units_TransitToProvinceId",
                table: "Units",
                column: "TransitToProvinceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiMemories");

            migrationBuilder.DropTable(
                name: "Buildings");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "DiplomaticRelations");

            migrationBuilder.DropTable(
                name: "NewsItems");

            migrationBuilder.DropTable(
                name: "ProvinceAdjacencies");

            migrationBuilder.DropTable(
                name: "ResearchProgress");

            migrationBuilder.DropTable(
                name: "UnitOrders");

            migrationBuilder.DropTable(
                name: "Units");

            migrationBuilder.DropTable(
                name: "Provinces");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "GameWorlds");
        }
    }
}
