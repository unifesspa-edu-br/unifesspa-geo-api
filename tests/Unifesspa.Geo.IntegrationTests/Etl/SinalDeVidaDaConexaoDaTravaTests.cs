namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Npgsql;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Efeito no banco: com o banco derrubando sessões paradas por mais de 3 segundos, só a conexão
/// que manda sinal de vida continua segurando a trava da carga.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class SinalDeVidaDaConexaoDaTravaTests
{
    private static readonly TimeSpan LimiteDeOciosidade = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PrazoDaQueda = TimeSpan.FromSeconds(20);

    private readonly GeoPostgisFixture _fixture;

    public SinalDeVidaDaConexaoDaTravaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Com sinal de vida a cada segundo, a trava continua ocupada depois do limite de ociosidade do banco")]
    public async Task ComSinalDeVida_TravaContinuaOcupada()
    {
        await using IAsyncDisposable trava = await AdquirirComOciosidadeLimitadaAsync(TimeSpan.FromSeconds(1));

        await Task.Delay(Espera);

        await ConferirTravaOcupadaAsync();
    }

    [Fact(DisplayName = "Sem sinal de vida, o banco derruba a conexão parada e a trava é liberada")]
    public async Task SemSinalDeVida_BancoLiberaATrava()
    {
        await using IAsyncDisposable trava = await AdquirirComOciosidadeLimitadaAsync(TimeSpan.Zero);

        bool liberada = await EsperarATravaFicarLivreAsync();

        liberada.Should().BeTrue("o banco encerra a sessão parada por mais de 3 segundos e libera a trava");
    }

    private async Task<IAsyncDisposable> AdquirirComOciosidadeLimitadaAsync(TimeSpan intervaloDoSinal)
    {
        NpgsqlConnectionStringBuilder cadeia = new(_fixture.ConnectionString)
        {
            Options = $"-c idle_session_timeout={(int)LimiteDeOciosidade.TotalSeconds}s",
        };
        EtlOpcoes opcoes = new() { IntervaloDoSinalDeVidaDaTrava = intervaloDoSinal };

        IAsyncDisposable? trava = await TravaDaCargaParaTeste.Criar(cadeia.ConnectionString, opcoes)
            .TentarAdquirirAsync(CancellationToken.None);

        trava.Should().NotBeNull("nenhuma outra sessão deve segurar a trava da carga neste momento");
        return trava!;
    }

    private async Task ConferirTravaOcupadaAsync()
    {
        IAsyncDisposable? outra = await TravaDaCargaParaTeste.Criar(_fixture.ConnectionString)
            .TentarAdquirirAsync(CancellationToken.None);
        try
        {
            outra.Should().BeNull("a conexão que manda sinal de vida segue segurando a trava");
        }
        finally
        {
            if (outra is not null)
            {
                await outra.DisposeAsync();
            }
        }
    }

    private async Task<bool> EsperarATravaFicarLivreAsync()
    {
        DateTimeOffset limite = DateTimeOffset.UtcNow + PrazoDaQueda;
        while (DateTimeOffset.UtcNow < limite)
        {
            if (await TravaEstavaLivreAsync())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return false;
    }

    private async Task<bool> TravaEstavaLivreAsync()
    {
        IAsyncDisposable? outra = await TravaDaCargaParaTeste.Criar(_fixture.ConnectionString)
            .TentarAdquirirAsync(CancellationToken.None);
        if (outra is not null)
        {
            await outra.DisposeAsync();
        }

        return outra is not null;
    }
}
