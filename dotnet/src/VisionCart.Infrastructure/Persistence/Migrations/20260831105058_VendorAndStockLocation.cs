using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisionCart.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VendorAndStockLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Aisle",
                table: "FrameVariant",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bin",
                table: "FrameVariant",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Shelf",
                table: "FrameVariant",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShelfRow",
                table: "FrameVariant",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Features",
                table: "Frame",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastCostMinor",
                table: "Frame",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromotionGrade",
                table: "Frame",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorId",
                table: "Frame",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorProductCode",
                table: "Frame",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Vendor",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    LeadTimeDays = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendor", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Frame_VendorId",
                table: "Frame",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_Code",
                table: "Vendor",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_Name",
                table: "Vendor",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Frame_Vendor_VendorId",
                table: "Frame",
                column: "VendorId",
                principalTable: "Vendor",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Frame_Vendor_VendorId",
                table: "Frame");

            migrationBuilder.DropTable(
                name: "Vendor");

            migrationBuilder.DropIndex(
                name: "IX_Frame_VendorId",
                table: "Frame");

            migrationBuilder.DropColumn(
                name: "Aisle",
                table: "FrameVariant");

            migrationBuilder.DropColumn(
                name: "Bin",
                table: "FrameVariant");

            migrationBuilder.DropColumn(
                name: "Shelf",
                table: "FrameVariant");

            migrationBuilder.DropColumn(
                name: "ShelfRow",
                table: "FrameVariant");

            migrationBuilder.DropColumn(
                name: "Features",
                table: "Frame");

            migrationBuilder.DropColumn(
                name: "LastCostMinor",
                table: "Frame");

            migrationBuilder.DropColumn(
                name: "PromotionGrade",
                table: "Frame");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "Frame");

            migrationBuilder.DropColumn(
                name: "VendorProductCode",
                table: "Frame");
        }
    }
}
