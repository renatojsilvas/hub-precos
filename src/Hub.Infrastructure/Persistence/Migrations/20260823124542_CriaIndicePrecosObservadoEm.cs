using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CriaIndicePrecosObservadoEm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_precos_observado_em",
                table: "precos",
                column: "observado_em");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_precos_observado_em",
                table: "precos");
        }
    }
}
