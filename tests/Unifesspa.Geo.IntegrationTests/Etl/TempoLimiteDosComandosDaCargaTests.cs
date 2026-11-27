namespace Unifesspa.Geo.IntegrationTests.Etl;

using System.Diagnostics;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Os comandos longos da carga em lote (mesclagem, contagem e índices) têm prazo próprio,
/// configurável em <c>Geo:Etl:TempoLimiteDosComandosDaCarga</c>: com a tabela de destino
/// bloqueada para gravação por outra sessão (a contagem passa, a mesclagem espera), a carga
/// falha pelo prazo configurado em vez de esperar o prazo padrão de 30 segundos da conexão
/// (sobre Postgres+PostGIS real).
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class TempoLimiteDosComandosDaCargaTests
{
    private readonly GeoPostgisFixture _fixture;

    public TempoLimiteDosComandosDaCargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Com a tabela de destino bloqueada, a carga falha pelo prazo configurado do comando")]
    public async Task TabelaBloqueada_FalhaPeloPrazoConfigurado()
    {
        BloqueioDeGravacaoDosComplementos bloqueio = await BloqueioDeGravacaoDosComplementos.AdquirirAsync(_fixture.ConnectionString);
        await using (bloqueio)
        {
            Func<Task> carga = () => ExecutarCargaVaziaAsync("00:00:02");

            Stopwatch cronometro = Stopwatch.StartNew();
            NpgsqlException falha = (await carga.Should().ThrowAsync<NpgsqlException>()).Which;
            cronometro.Stop();

            falha.InnerException.Should().BeOfType<TimeoutException>();
            cronometro.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
        }
    }

    [Theory(DisplayName = "Prazo do comando da carga igual a zero, negativo ou grande demais para segundos inteiros é recusado na configuração")]
    [InlineData("00:00:00")]
    [InlineData("-00:00:05")]
    [InlineData("30000.00:00:00")]
    public void PrazoNaoPositivo_ERecusado(string prazo)
    {
        Func<ImportadorDeLogradourosPorCopia> criar = () => CriarImportador(_fixture.CreateDbContext(), prazo);

        criar.Should().Throw<InvalidOperationException>()
            .WithMessage("*TempoLimiteDosComandosDaCarga*maior que zero*");
    }

    private async Task ExecutarCargaVaziaAsync(string prazo)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        await using IDbContextTransaction transacao = await contexto.Database.BeginTransactionAsync();
        GeoImportadorLocalidades importadorDasFolhas = new(
            new GeoImportadorDistritoBairro(contexto),
            CriarImportador(contexto, prazo),
            NullLogger<GeoImportadorLocalidades>.Instance);

        await using FonteEmMemoria fonte = new();
        await importadorDasFolhas.ImportarAsync(fonte, CancellationToken.None);
        await transacao.CommitAsync();
    }

    private static ImportadorDeLogradourosPorCopia CriarImportador(GeoDbContext contexto, string prazo)
    {
        IConfiguration configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.TempoLimiteDosComandosDaCarga)}"] = prazo,
            })
            .Build();

        return new ImportadorDeLogradourosPorCopia(contexto, configuracao, new RelogioFixo(), NullLogger<ImportadorDeLogradourosPorCopia>.Instance);
    }
}
