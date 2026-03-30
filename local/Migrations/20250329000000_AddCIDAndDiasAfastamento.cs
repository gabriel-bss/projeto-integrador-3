using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtestadoMedico.Migrations
{
    /// <inheritdoc />
    public partial class AddCIDAndDiasAfastamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CID",
                table: "Atestados",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiasAfastamento",
                table: "Atestados",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CID",
                table: "Atestados");

            migrationBuilder.DropColumn(
                name: "DiasAfastamento",
                table: "Atestados");
        }
    }
}
