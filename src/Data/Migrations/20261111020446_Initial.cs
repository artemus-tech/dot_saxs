using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DotSaxs.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CacheEntries",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Function = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    DatasetHash = table.Column<string>(type: "text", nullable: false),
                    ParametersHash = table.Column<string>(type: "text", nullable: false),
                    ResultFile = table.Column<string>(type: "text", nullable: false),
                    ResultSha256 = table.Column<string>(type: "text", nullable: false),
                    ComputeMilliseconds = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheEntries", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "QueueItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Worker = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReferenceSets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Numpy = table.Column<string>(type: "text", nullable: false),
                    Scipy = table.Column<string>(type: "text", nullable: false),
                    Python = table.Column<string>(type: "text", nullable: false),
                    Environment = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Commit = table.Column<string>(type: "text", nullable: false),
                    Machine = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SetId = table.Column<long>(type: "bigint", nullable: false),
                    Python = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Match = table.Column<string>(type: "jsonb", nullable: false),
                    Call = table.Column<string>(type: "jsonb", nullable: false),
                    ErrorType = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cases_ReferenceSets_SetId",
                        column: x => x.SetId,
                        principalTable: "ReferenceSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Arrays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CaseId = table.Column<long>(type: "bigint", nullable: false),
                    Place = table.Column<string>(type: "text", nullable: false),
                    File = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Arrays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Arrays_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    Test = table.Column<string>(type: "text", nullable: false),
                    CaseId = table.Column<long>(type: "bigint", nullable: true),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    Milliseconds = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestResults_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TestResults_TestRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "TestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Arrays_CaseId_Place",
                table: "Arrays",
                columns: new[] { "CaseId", "Place" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Arrays_Sha256",
                table: "Arrays",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_SetId_Python_Name",
                table: "Cases",
                columns: new[] { "SetId", "Python", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueItems_Status_Kind_Id",
                table: "QueueItems",
                columns: new[] { "Status", "Kind", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceSets_ContentHash",
                table: "ReferenceSets",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestResults_CaseId",
                table: "TestResults",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_TestResults_RunId",
                table: "TestResults",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Arrays");

            migrationBuilder.DropTable(
                name: "CacheEntries");

            migrationBuilder.DropTable(
                name: "QueueItems");

            migrationBuilder.DropTable(
                name: "TestResults");

            migrationBuilder.DropTable(
                name: "Cases");

            migrationBuilder.DropTable(
                name: "TestRuns");

            migrationBuilder.DropTable(
                name: "ReferenceSets");
        }
    }
}
