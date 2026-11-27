namespace Unifesspa.Geo.IntegrationTests;

using System.Data;
using System.Data.Common;
using System.Net;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Prova que, com todas as migrações aplicadas, o banco do Geo não tem mais a tabela
/// <c>idempotency_cache</c>, e que as demais tabelas continuam as do modelo. Também prova que o host da API
/// sobe com esse esquema e que os health checks respondem 200; os serviços em segundo plano da carga ficam
/// desligados na fábrica e não são exercitados aqui.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class RetiradaDaTabelaDeIdempotenciaTests
{
    private const string TabelaRetirada = "idempotency_cache";
    private const string TabelaDoPostgis = "spatial_ref_sys";
    private const string TabelaDoHistorico = "__EFMigrationsHistory";

    private readonly GeoPostgisFixture _fixture;

    public RetiradaDaTabelaDeIdempotenciaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Com todas as migrações aplicadas, a tabela idempotency_cache não existe")]
    public async Task TabelaDeIdempotencia_NaoExisteAposAsMigracoes()
    {
        string[] tabelas = await ListarTabelasDoBancoAsync();

        tabelas.Should().NotContain(TabelaRetirada);
    }

    [Fact(DisplayName = "As tabelas do banco são exatamente as do modelo, mais o histórico e a do PostGIS")]
    public async Task TabelasDoBanco_SaoAsDoModelo()
    {
        string[] doBanco = await ListarTabelasDoBancoAsync();
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        string[] doModelo = [.. contexto.Model.GetEntityTypes()
            .Select(tipo => tipo.GetTableName())
            .OfType<string>()];

        doBanco.Except([TabelaDoPostgis, TabelaDoHistorico]).Should().BeEquivalentTo(doModelo);
    }

    [Fact(DisplayName = "O modelo não tem mais nenhuma tabela de idempotência e não há mudança de modelo pendente")]
    public async Task Modelo_NaoTemIdempotenciaENaoTemMudancaPendente()
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();

        contexto.Model.GetEntityTypes().Select(tipo => tipo.GetTableName())
            .Should().NotContain(TabelaRetirada);
        contexto.Database.HasPendingModelChanges().Should().BeFalse(
            "a migração nova deve deixar o esquema igual ao modelo, sem outra diferença");
    }

    [Fact(DisplayName = "O host da API sobe com o esquema sem a tabela idempotency_cache e os health checks respondem 200")]
    public async Task Host_SobeSemATabelaDeIdempotenciaEHealthChecksRespondem200()
    {
        using HttpClient cliente = _fixture.Factory.CreateClient();

        HttpResponseMessage prontidao = await cliente.GetAsync(new Uri("/health/ready", UriKind.Relative));
        HttpResponseMessage vida = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative));

        prontidao.StatusCode.Should().Be(HttpStatusCode.OK);
        vida.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<string[]> ListarTabelasDoBancoAsync()
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        await using DbCommand comando = (await AbrirConexaoAsync(contexto)).CreateCommand();
        comando.CommandText = "SELECT table_name FROM information_schema.tables "
            + "WHERE table_schema = 'public' AND table_type = 'BASE TABLE';";

        List<string> tabelas = [];
        await using DbDataReader leitor = await comando.ExecuteReaderAsync();
        while (await leitor.ReadAsync())
        {
            tabelas.Add(leitor.GetString(0));
        }

        return [.. tabelas];
    }

    private static async Task<DbConnection> AbrirConexaoAsync(GeoDbContext contexto)
    {
        DbConnection conexao = contexto.Database.GetDbConnection();
        if (conexao.State != ConnectionState.Open)
        {
            await conexao.OpenAsync();
        }

        return conexao;
    }
}
