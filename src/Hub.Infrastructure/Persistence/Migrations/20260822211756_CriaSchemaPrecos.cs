using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CriaSchemaPrecos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instrumentos",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    classe = table.Column<string>(type: "text", nullable: false),
                    nome_exibicao = table.Column<string>(type: "text", nullable: false),
                    ativo_desde = table.Column<DateOnly>(type: "date", nullable: true),
                    ativo_ate = table.Column<DateOnly>(type: "date", nullable: true),
                    paga_cupom = table.Column<bool>(type: "boolean", nullable: false),
                    metadados = table.Column<string>(type: "jsonb", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instrumentos", x => x.id);
                    table.CheckConstraint("ck_instrumentos_classe_prefixo", "classe = split_part(id, ':', 1)");
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    routing_key = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    publicado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "instrumento_fontes",
                columns: table => new
                {
                    instrumento_id = table.Column<string>(type: "text", nullable: false),
                    fonte = table.Column<string>(type: "text", nullable: false),
                    codigo_na_fonte = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instrumento_fontes", x => new { x.instrumento_id, x.fonte });
                    table.ForeignKey(
                        name: "FK_instrumento_fontes_instrumentos_instrumento_id",
                        column: x => x.instrumento_id,
                        principalTable: "instrumentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "precos",
                columns: table => new
                {
                    instrumento_id = table.Column<string>(type: "text", nullable: false),
                    data_ref = table.Column<DateOnly>(type: "date", nullable: false),
                    campo = table.Column<string>(type: "text", nullable: false),
                    fonte = table.Column<string>(type: "text", nullable: false),
                    revisao = table.Column<int>(type: "integer", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    observado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_precos", x => new { x.instrumento_id, x.data_ref, x.campo, x.fonte, x.revisao });
                    table.ForeignKey(
                        name: "FK_precos_instrumentos_instrumento_id",
                        column: x => x.instrumento_id,
                        principalTable: "instrumentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_instrumento_fontes_fonte_codigo",
                table: "instrumento_fontes",
                columns: new[] { "fonte", "codigo_na_fonte" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instrumentos_classe",
                table: "instrumentos",
                column: "classe");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pendentes",
                table: "outbox",
                column: "id",
                filter: "publicado_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_precos_lookup",
                table: "precos",
                columns: new[] { "instrumento_id", "data_ref" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instrumento_fontes");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "precos");

            migrationBuilder.DropTable(
                name: "instrumentos");
        }
    }
}
