using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Unifesspa.Geo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RegistraOPacoteRecebidoNaExecucao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pacote_sha256",
                table: "geo_importacao_execucao",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "pacote_tamanho_em_bytes",
                table: "geo_importacao_execucao",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_geo_importacao_execucao_status_concluido_em",
                table: "geo_importacao_execucao",
                columns: new[] { "status", "concluido_em" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_geo_importacao_execucao_status_concluido_em",
                table: "geo_importacao_execucao");

            migrationBuilder.DropColumn(
                name: "pacote_sha256",
                table: "geo_importacao_execucao");

            migrationBuilder.DropColumn(
                name: "pacote_tamanho_em_bytes",
                table: "geo_importacao_execucao");
        }
    }
}
