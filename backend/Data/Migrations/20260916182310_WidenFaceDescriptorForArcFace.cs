using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WidenFaceDescriptorForArcFace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ArcFace templates are 512 floats (2048 bytes) — the old column fitted them with nothing
            // to spare. Widened for headroom, but kept under 8000 bytes so the column stays in-row.
            migrationBuilder.AlterColumn<byte[]>(
                name: "Descriptor",
                table: "VisitorFaceDescriptors",
                type: "varbinary(4096)",
                maxLength: 4096,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(2048)",
                oldMaxLength: 2048);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "Descriptor",
                table: "VisitorFaceDescriptors",
                type: "varbinary(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(4096)",
                oldMaxLength: 4096);
        }
    }
}
