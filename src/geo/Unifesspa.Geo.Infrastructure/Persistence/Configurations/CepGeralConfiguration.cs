namespace Unifesspa.Geo.Infrastructure.Persistence.Configurations;

using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Mapeamento de <see cref="CepGeral"/> — CEP oficial de cidade ou distrito. A chave
/// natural <c>cep</c> é UNIQUE (base do upsert idempotente do ETL). FKs intra-banco
/// para <c>cidade</c> (obrigatória) e <c>distrito</c> (opcional) — ADR-0054; coordenada
/// geography com GIST (ADR-0091).
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via EF Core ModelBuilder.ApplyConfigurationsFromAssembly por reflection.")]
internal sealed class CepGeralConfiguration : IEntityTypeConfiguration<CepGeral>
{
    public void Configure(EntityTypeBuilder<CepGeral> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cep_geral");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Cep).IsRequired();
        builder.Property(g => g.Uf).IsRequired();
        builder.Property(g => g.Latitude).HasPrecision(9, 6);
        builder.Property(g => g.Longitude).HasPrecision(9, 6);
        builder.ConfigurarCoordenada(g => g.Coordenada);

        builder.HasOne<Cidade>()
            .WithMany()
            .HasForeignKey(g => g.CidadeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Distrito>()
            .WithMany()
            .HasForeignKey(g => g.DistritoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(g => g.Cep)
            .IsUnique()
            .HasDatabaseName("ix_cep_geral_cep");

        builder.HasIndex(g => g.CidadeId)
            .HasDatabaseName("ix_cep_geral_cidade_id");

        builder.HasIndex(g => g.DistritoId)
            .HasDatabaseName("ix_cep_geral_distrito_id");

        builder.HasIndex(g => g.Coordenada)
            .HasMethod("gist")
            .HasDatabaseName("ix_cep_geral_coordenada");

        builder.ConfigurarProveniencia(g => g.VersaoDataset, g => g.Vigente);
        builder.HasIndex(g => g.VersaoDataset)
            .HasDatabaseName("ix_cep_geral_versao_dataset");
    }
}
