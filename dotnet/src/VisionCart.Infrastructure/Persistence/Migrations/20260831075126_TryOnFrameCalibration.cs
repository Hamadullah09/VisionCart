using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisionCart.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TryOnFrameCalibration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "TryOnFrontLeftX",
                table: "FrameVariant",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TryOnFrontRightX",
                table: "FrameVariant",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TryOnImageHeight",
                table: "FrameVariant",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TryOnImageWidth",
                table: "FrameVariant",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TryOnLensBottomY",
                table: "FrameVariant",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TryOnLensTopY",
                table: "FrameVariant",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TryOnFrontLeftX",
                table: "FrameVariant");

            migrationBuilder.DropColumn(
                name: "TryOnFrontRightX",
                table: "FrameVariant");

            migrationBuilder.DropColumn(
                name: "TryOnImageHeight",
                table: "FrameVariant");

            migrationBuilder.DropColumn(
                name: "TryOnImageWidth",
                table: "FrameVariant");

            migrationBuilder.DropColumn(
                name: "TryOnLensBottomY",
                table: "FrameVariant");

            migrationBuilder.DropColumn(
                name: "TryOnLensTopY",
                table: "FrameVariant");
        }
    }
}
