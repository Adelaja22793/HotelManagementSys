using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PublicBookingEnabled",
                table: "Branches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BookingChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingChannels_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccessTokenHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ArrivalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DepartureDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Adults = table.Column<int>(type: "int", nullable: false),
                    Children = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiryExtended = table.Column<bool>(type: "bit", nullable: false),
                    ClientFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    GuestFirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GuestLastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GuestEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    GuestPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    GuestCountry = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    SpecialRequests = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TermsAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConvertedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReviewReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingHolds", x => x.Id);
                    table.CheckConstraint("CK_BookingHolds_Dates", "[DepartureDate] > [ArrivalDate]");
                    table.CheckConstraint("CK_BookingHolds_Guests", "[Adults] >= 1 AND [Children] >= 0");
                    table.ForeignKey(
                        name: "FK_BookingHolds_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChannelReservationMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelReservationMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelReservationMappings_BookingChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "BookingChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChannelReservationMappings_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingHoldRooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HoldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoomTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatePlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    RoomSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DepositRequired = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    QuoteJson = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingHoldRooms", x => x.Id);
                    table.CheckConstraint("CK_BookingHoldRooms_Quantity", "[Quantity] BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_BookingHoldRooms_BookingHolds_HoldId",
                        column: x => x.HoldId,
                        principalTable: "BookingHolds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingHoldRooms_RatePlans_RatePlanId",
                        column: x => x.RatePlanId,
                        principalTable: "RatePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingHoldRooms_RoomTypes_RoomTypeId",
                        column: x => x.RoomTypeId,
                        principalTable: "RoomTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingPaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HoldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MerchantReference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AuthorizationUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReportedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ReportedCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    ProviderStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPaymentAttempts", x => x.Id);
                    table.CheckConstraint("CK_BookingPaymentAttempts_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_BookingPaymentAttempts_BookingHolds_HoldId",
                        column: x => x.HoldId,
                        principalTable: "BookingHolds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingChannels_CompanyId_Code",
                table: "BookingChannels",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingHoldRooms_HoldId_RoomTypeId",
                table: "BookingHoldRooms",
                columns: new[] { "HoldId", "RoomTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingHoldRooms_RatePlanId",
                table: "BookingHoldRooms",
                column: "RatePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingHoldRooms_RoomTypeId",
                table: "BookingHoldRooms",
                column: "RoomTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingHolds_BranchId",
                table: "BookingHolds",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingHolds_ClientFingerprint_Status",
                table: "BookingHolds",
                columns: new[] { "ClientFingerprint", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingHolds_Reference",
                table: "BookingHolds",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingHolds_Status_ExpiresAtUtc",
                table: "BookingHolds",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingPaymentAttempts_BranchId_Status",
                table: "BookingPaymentAttempts",
                columns: new[] { "BranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingPaymentAttempts_HoldId",
                table: "BookingPaymentAttempts",
                column: "HoldId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingPaymentAttempts_MerchantReference",
                table: "BookingPaymentAttempts",
                column: "MerchantReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelReservationMappings_BranchId_ChannelId_ExternalReference",
                table: "ChannelReservationMappings",
                columns: new[] { "BranchId", "ChannelId", "ExternalReference" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelReservationMappings_ChannelId",
                table: "ChannelReservationMappings",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelReservationMappings_ReservationId",
                table: "ChannelReservationMappings",
                column: "ReservationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingHoldRooms");

            migrationBuilder.DropTable(
                name: "BookingPaymentAttempts");

            migrationBuilder.DropTable(
                name: "ChannelReservationMappings");

            migrationBuilder.DropTable(
                name: "BookingHolds");

            migrationBuilder.DropTable(
                name: "BookingChannels");

            migrationBuilder.DropColumn(
                name: "PublicBookingEnabled",
                table: "Branches");
        }
    }
}
