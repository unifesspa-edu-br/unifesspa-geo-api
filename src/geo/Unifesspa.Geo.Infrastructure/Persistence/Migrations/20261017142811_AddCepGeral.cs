using System;

using Microsoft.EntityFrameworkCore.Migrations;

using NetTopologySuite.Geometries;

#nullable disable

namespace Unifesspa.Geo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCepGeral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cep_geral",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cep = table.Column<string>(type: "text", nullable: false),
                    cidade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    distrito_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uf = table.Column<string>(type: "text", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    coordenada = table.Column<Point>(type: "geography (Point, 4326)", nullable: true),
                    versao_dataset = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vigente = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cep_geral", x => x.id);
                    table.ForeignKey(
                        name: "fk_cep_geral_cidades_cidade_id",
                        column: x => x.cidade_id,
                        principalTable: "cidade",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cep_geral_distritos_distrito_id",
                        column: x => x.distrito_id,
                        principalTable: "distrito",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cep_geral_cep",
                table: "cep_geral",
                column: "cep",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cep_geral_cidade_id",
                table: "cep_geral",
                column: "cidade_id");

            migrationBuilder.CreateIndex(
                name: "ix_cep_geral_coordenada",
                table: "cep_geral",
                column: "coordenada")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_cep_geral_distrito_id",
                table: "cep_geral",
                column: "distrito_id");

            migrationBuilder.CreateIndex(
                name: "ix_cep_geral_versao_dataset",
                table: "cep_geral",
                column: "versao_dataset");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cep_geral");
        }
    }
}
