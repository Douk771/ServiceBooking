using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderDailyCounters",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderDailyCounters", x => new { x.CompanyId, x.BusinessDate });
                    table.ForeignKey(
                        name: "FK_OrderDailyCounters_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PublicToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CustomerKind = table.Column<int>(type: "integer", nullable: false),
                    CustomerUserId = table.Column<string>(type: "text", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CustomerPhoneVerified = table.Column<bool>(type: "boolean", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcceptanceModeSnapshot = table.Column<int>(type: "integer", nullable: false),
                    AllowCustomerCancelSnapshot = table.Column<bool>(type: "boolean", nullable: false),
                    CustomerModeSnapshot = table.Column<int>(type: "integer", nullable: false),
                    EstimatedTotal = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    FinalTotal = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    HasWeightItems = table.Column<bool>(type: "boolean", nullable: false),
                    IsModifiedByShop = table.Column<bool>(type: "boolean", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsentPrivacyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConsentTermsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConsentAcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckoutNoticeVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PersonalDataErased = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadyAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_AspNetUsers_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Orders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductCategories_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShopSettings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerMode = table.Column<int>(type: "integer", nullable: false),
                    AcceptanceMode = table.Column<int>(type: "integer", nullable: false),
                    AllowCustomerCancel = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    TrackStock = table.Column<bool>(type: "boolean", nullable: false),
                    OrdersRevision = table.Column<long>(type: "bigint", nullable: false),
                    SellerLegalForm = table.Column<int>(type: "integer", nullable: true),
                    SellerLegalName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SellerInn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    SellerOgrn = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    SellerLegalAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopSettings", x => x.CompanyId);
                    table.ForeignKey(
                        name: "FK_ShopSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorKind = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<string>(type: "text", nullable: true),
                    ActorNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ChangesJson = table.Column<string>(type: "jsonb", nullable: true),
                    TotalBefore = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    TotalAfter = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    VisibleToCustomer = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderEvents_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Unit = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    PortionText = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WeightStepGrams = table.Column<int>(type: "integer", nullable: true),
                    MinQuantityGrams = table.Column<int>(type: "integer", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    IsSoldOut = table.Column<bool>(type: "boolean", nullable: false),
                    CompositionAndAllergens = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StockOnHand = table.Column<int>(type: "integer", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Products_StockOnHand_NonNegative", "\"StockOnHand\" IS NULL OR \"StockOnHand\" >= 0");
                    table.ForeignKey(
                        name: "FK_Products_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_ProductCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ProductCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    NameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    PortionTextSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WeightStepGrams = table.Column<int>(type: "integer", nullable: true),
                    QuantityOrdered = table.Column<int>(type: "integer", nullable: false),
                    QuantityActual = table.Column<int>(type: "integer", nullable: true),
                    LineTotalEstimated = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    LineTotalFinal = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    ReservesStock = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderEvents_OccurredAtUtc",
                table: "OrderEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OrderEvents_OrderId_OccurredAtUtc",
                table: "OrderEvents",
                columns: new[] { "OrderId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_BusinessDate_Number",
                table: "Orders",
                columns: new[] { "CompanyId", "BusinessDate", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_CompletedAtUtc",
                table: "Orders",
                columns: new[] { "CompanyId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_IdempotencyKey",
                table: "Orders",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_Status",
                table: "Orders",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerPhone_CreatedAtUtc",
                table: "Orders",
                columns: new[] { "CustomerPhone", "CreatedAtUtc" },
                filter: "\"CustomerPhone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerUserId_CreatedAtUtc",
                table: "Orders",
                columns: new[] { "CustomerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PublicToken",
                table: "Orders",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_CompanyId_Position",
                table: "ProductCategories",
                columns: new[] { "CompanyId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanyId_CategoryId_Position",
                table: "Products",
                columns: new[] { "CompanyId", "CategoryId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanyId_Live",
                table: "Products",
                column: "CompanyId",
                filter: "\"DeletedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderDailyCounters");

            migrationBuilder.DropTable(
                name: "OrderEvents");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "ShopSettings");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "ProductCategories");
        }
    }
}
