namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>Preparo e conferências do banco da coleção usadas pelos testes da carga.</summary>
internal sealed class BancoDaCarga
{
    private readonly GeoPostgisFixture _fixture;

    public BancoDaCarga(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task LimparAsync()
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        await contexto.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE pais, logradouro_complemento, cep_grande_usuario, geo_importacao_execucao CASCADE");
    }

    public async Task<GeoImportacaoExecucao> ExecucaoAsync(Guid id)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return await leitura.ImportacaoExecucoes.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    /// <summary>Linhas das tabelas da carga que uma conexão independente consegue enxergar neste instante.</summary>
    public async Task<long> LinhasVisiveisParaOutraConexaoAsync()
    {
        await using NpgsqlConnection conexao = new(_fixture.ConnectionString);
        await conexao.OpenAsync();
        await using NpgsqlCommand comando = conexao.CreateCommand();
        comando.CommandText = """
            SELECT (SELECT count(*) FROM pais) + (SELECT count(*) FROM cidade)
                 + (SELECT count(*) FROM distrito) + (SELECT count(*) FROM logradouro_complemento)
                 + (SELECT count(*) FROM logradouro)
                 + (SELECT count(*) FROM geo_importacao_execucao WHERE status = 1)
            """;
        return (long)(await comando.ExecuteScalarAsync())!;
    }

    /// <summary>Confere que só a versão anterior (202601) está publicada, sem nenhum rastro da seguinte.</summary>
    public async Task ConferirSomenteVersaoAnteriorAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.Cidades.CountAsync(c => c.VersaoDataset == VersoesFicticias.VersaoAnterior && c.Vigente))
            .Should().Be(2, "as duas cidades da versão anterior seguem vigentes");
        (await leitura.Logradouros.CountAsync()).Should().Be(3);
        (await leitura.Logradouros.CountAsync(l => l.VersaoDataset != VersoesFicticias.VersaoAnterior || !l.Vigente))
            .Should().Be(0, "nenhum logradouro foi atualizado nem marcado como não vigente");
        (await leitura.LogradouroComplementos.Select(c => c.Complemento).ToListAsync())
            .Should().BeEquivalentTo(["lado par"], "o complemento da versão nova foi desfeito");
    }
}
