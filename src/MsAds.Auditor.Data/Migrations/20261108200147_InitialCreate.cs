using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MsAds.Auditor.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "change_runs",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RanAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RanBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AccountId = table.Column<long>(type: "INTEGER", nullable: false),
                    AccountName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    WasPreview = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_change_runs", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "scan_runs",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    IsDemo = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailureMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_runs", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    TargetCpa = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    TargetRoas = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    MinimumSpend = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    MinimumClicks = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumConversions = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    CpaToleranceAboveTargetPercent = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    SignificantChangePercent = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    CtrDeclinePercent = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    SearchTermMinimumSpend = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    SearchTermMinimumClicks = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    CultureName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MinimumImpressionsForCtr = table.Column<int>(type: "INTEGER", nullable: false),
                    ImpressionShareLostToBudgetPercent = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    DeliveryDropPercent = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    CpaVersusAccountMultiple = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    ChangeImpactWindowDays = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.Id);
                    table.CheckConstraint("ck_settings_singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "change_outcomes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ChangeId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Risk = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ChangeAccountId = table.Column<long>(type: "INTEGER", nullable: false),
                    CustomerId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetEntityId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetDescription = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    MatchType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SpendAtStake = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Caveats = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_change_outcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_change_outcomes_change_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "change_runs",
                        principalColumn: "RunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan_verdicts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<long>(type: "INTEGER", nullable: false),
                    CustomerId = table.Column<long>(type: "INTEGER", nullable: false),
                    AccountName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Verdict = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BasedOnConversions = table.Column<bool>(type: "INTEGER", nullable: false),
                    Failed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_verdicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scan_verdicts_scan_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "scan_runs",
                        principalColumn: "RunId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_change_outcomes_ChangeAccountId_Status",
                table: "change_outcomes",
                columns: new[] { "ChangeAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_change_outcomes_RunId_Ordinal",
                table: "change_outcomes",
                columns: new[] { "RunId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_change_runs_AccountId_WasPreview",
                table: "change_runs",
                columns: new[] { "AccountId", "WasPreview" });

            migrationBuilder.CreateIndex(
                name: "IX_change_runs_RanAt",
                table: "change_runs",
                column: "RanAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_scan_runs_GeneratedAt",
                table: "scan_runs",
                column: "GeneratedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_scan_verdicts_AccountId",
                table: "scan_verdicts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_scan_verdicts_RunId_AccountId",
                table: "scan_verdicts",
                columns: new[] { "RunId", "AccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_outcomes");

            migrationBuilder.DropTable(
                name: "scan_verdicts");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "change_runs");

            migrationBuilder.DropTable(
                name: "scan_runs");
        }
    }
}
