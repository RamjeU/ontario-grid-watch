using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GridWatch.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Demand",
                columns: table => new
                {
                    HourStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OntarioDemandMw = table.Column<int>(type: "int", nullable: false),
                    MarketDemandMw = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Demand", x => x.HourStartUtc);
                });

            migrationBuilder.CreateTable(
                name: "FuelOutput",
                columns: table => new
                {
                    HourStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Fuel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OutputMw = table.Column<int>(type: "int", nullable: true),
                    MissingPoints = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelOutput", x => new { x.HourStartUtc, x.Fuel });
                });

            migrationBuilder.CreateTable(
                name: "IngestionRun",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowsInserted = table.Column<int>(type: "int", nullable: false),
                    RowsUpdated = table.Column<int>(type: "int", nullable: false),
                    Warnings = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionRun", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LiveDemand",
                columns: table => new
                {
                    IntervalStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OntarioDemandMw = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveDemand", x => x.IntervalStartUtc);
                });

            migrationBuilder.CreateTable(
                name: "Price",
                columns: table => new
                {
                    IntervalStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PricePerMwh = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Price", x => x.IntervalStartUtc);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionRun_StartedAtUtc",
                table: "IngestionRun",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Demand");

            migrationBuilder.DropTable(
                name: "FuelOutput");

            migrationBuilder.DropTable(
                name: "IngestionRun");

            migrationBuilder.DropTable(
                name: "LiveDemand");

            migrationBuilder.DropTable(
                name: "Price");
        }
    }
}
