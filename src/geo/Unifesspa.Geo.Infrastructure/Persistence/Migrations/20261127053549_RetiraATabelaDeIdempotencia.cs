using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Unifesspa.Geo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetiraATabelaDeIdempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_cache");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_cache",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    body_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    response_body_cipher = table.Column<byte[]>(type: "bytea", nullable: true),
                    response_headers_json = table.Column<string>(type: "jsonb", nullable: true),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    scope = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_cache", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_idempotency_expires_at",
                table: "idempotency_cache",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "idx_idempotency_lookup",
                table: "idempotency_cache",
                columns: new[] { "scope", "endpoint", "idempotency_key" },
                unique: true);
        }
    }
}
