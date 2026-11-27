namespace Unifesspa.Geo.IntegrationTests.Etl;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Executa as cargas de <see cref="FonteComTodosOsCampos"/> sobre o banco da coleção e localiza o
/// identificador de uma linha pela chave natural. Usado por composição pelas classes de teste.
/// </summary>
internal sealed class CargaDosValoresGravados
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDasLocalidades _cenario;

    public CargaDosValoresGravados(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDasLocalidades(fixture);
    }

    public Task CarregarAPrimeiraAsync() => CarregarDoZeroAsync(FonteComTodosOsCampos.Primeira());

    public Task CarregarComChaveRepetidaAsync() => CarregarDoZeroAsync(FonteComTodosOsCampos.ComChaveRepetida());

    /// <summary>Segunda carga sobre a primeira, sem limpar o banco: as mesmas chaves chegam com valores trocados.</summary>
    public async Task CarregarASegundaAsync()
    {
        FonteEmMemoria segunda = FonteComTodosOsCampos.Segunda();
        await CargaPelaEtl.ExecutarAsync(_fixture, segunda);
        await _cenario.ExecutarLocalidadesAsync(segunda);
    }

    public async Task<Guid> PaisIdAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return (await leitura.Paises.SingleAsync()).Id;
    }

    public async Task<Guid> EstadoIdAsync(string uf)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return (await leitura.Estados.SingleAsync(e => e.Uf == uf)).Id;
    }

    public async Task<Guid> CidadeIdAsync(string codigoIbge)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return (await leitura.Cidades.SingleAsync(c => c.CodigoIbge == codigoIbge)).Id;
    }

    public async Task<Guid> DistritoIdAsync(string nomeNormalizado)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return (await leitura.Distritos.SingleAsync(d => d.NomeNormalizado == nomeNormalizado)).Id;
    }

    public async Task<Guid> BairroIdAsync(string nomeNormalizado)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return (await leitura.Bairros.SingleAsync(b => b.NomeNormalizado == nomeNormalizado)).Id;
    }

    private async Task CarregarDoZeroAsync(FonteEmMemoria fonte)
    {
        await _cenario.PrepararTopoAsync(fonte);
        await _cenario.ExecutarLocalidadesAsync(fonte);
    }
}
