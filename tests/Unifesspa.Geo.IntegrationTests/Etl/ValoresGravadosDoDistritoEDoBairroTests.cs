namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Fixa o que a carga grava em cada coluna do distrito e do bairro (exceto o identificador e os
/// instantes): nome aparado, forma de busca em minúsculas que compõe a chave, UF em maiúsculas,
/// origem na base dos Correios, coordenada tirada do ponto validado, segunda carga e chave repetida.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ValoresGravadosDoDistritoEDoBairroTests
{
    private const string CodigoDeMaraba = "1500402";
    private const string CodigoDeSaoPaulo = "3550308";

    private readonly GeoPostgisFixture _fixture;
    private readonly CargaDosValoresGravados _carga;

    public ValoresGravadosDoDistritoEDoBairroTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _carga = new CargaDosValoresGravados(fixture);
    }

    [Fact(DisplayName = "Primeira carga: os distritos gravam nome, forma de busca, UF, origem e a coordenada válida ou ausente")]
    public async Task PrimeiraCarga_GravaOsDistritos()
    {
        await _carga.CarregarAPrimeiraAsync();

        Guid maraba = await _carga.CidadeIdAsync(CodigoDeMaraba);
        Guid saoPaulo = await _carga.CidadeIdAsync(CodigoDeSaoPaulo);
        (await LerDistritosAsync()).Should().Equal(
            new LocalidadeGravada(saoPaulo, "SP", "Centro", "centro", PosicaoGravada.Ausente, "20", "202601", true),
            new LocalidadeGravada(maraba, "PA", "Cidade Nova", "cidade nova", PosicaoGravada.Valida(-5.36m, -49.11m), "10", "202601", true));
    }

    [Fact(DisplayName = "Primeira carga: os bairros gravam nome, forma de busca, UF, origem e a coordenada válida ou ausente")]
    public async Task PrimeiraCarga_GravaOsBairros()
    {
        await _carga.CarregarAPrimeiraAsync();

        Guid maraba = await _carga.CidadeIdAsync(CodigoDeMaraba);
        Guid saoPaulo = await _carga.CidadeIdAsync(CodigoDeSaoPaulo);
        (await LerBairrosAsync()).Should().Equal(
            new LocalidadeGravada(maraba, "PA", "Bairro Sem Origem", "bairro sem origem", PosicaoGravada.Ausente, null, "202601", true),
            new LocalidadeGravada(maraba, "PA", "Cidade Nova", "cidade nova", PosicaoGravada.Valida(-5.37m, -49.12m), "100", "202601", true),
            new LocalidadeGravada(saoPaulo, "SP", "Sé", "se", PosicaoGravada.Ausente, "200", "202601", true));
    }

    [Fact(DisplayName = "Segunda carga: distritos e bairros trocam os valores, mantêm a chave e o identificador e gravam a nova versão")]
    public async Task SegundaCarga_AtualizaOsDistritosEOsBairros()
    {
        await _carga.CarregarAPrimeiraAsync();
        Guid distritoAntes = await _carga.DistritoIdAsync("cidade nova");
        Guid bairroAntes = await _carga.BairroIdAsync("cidade nova");

        await _carga.CarregarASegundaAsync();

        await ConferirOsValoresDaSegundaAsync();
        (await _carga.DistritoIdAsync("cidade nova")).Should().Be(distritoAntes);
        (await _carga.BairroIdAsync("cidade nova")).Should().Be(bairroAntes);
    }

    [Fact(DisplayName = "Chave repetida na fonte: a última ocorrência de cada distrito e de cada bairro vale")]
    public async Task ChaveRepetida_UltimaOcorrenciaVale()
    {
        await _carga.CarregarComChaveRepetidaAsync();

        await ConferirOsValoresDaSegundaAsync();
    }

    private async Task ConferirOsValoresDaSegundaAsync()
    {
        Guid maraba = await _carga.CidadeIdAsync(CodigoDeMaraba);
        Guid saoPaulo = await _carga.CidadeIdAsync(CodigoDeSaoPaulo);
        (await LerDistritosAsync()).Should().Equal(
            new LocalidadeGravada(saoPaulo, "SP", "Centro Histórico", "centro", PosicaoGravada.Valida(-23.55m, -46.63m), "20", "202602", true),
            new LocalidadeGravada(maraba, "PA", "Cidade Nova (núcleo)", "cidade nova", PosicaoGravada.Valida(-5.40m, -49.15m), "10", "202602", true));
        (await LerBairrosAsync()).Should().Equal(
            new LocalidadeGravada(maraba, "PA", "Bairro Sem Origem", "bairro sem origem", PosicaoGravada.Ausente, "300", "202602", true),
            new LocalidadeGravada(maraba, "PA", "Cidade Nova II", "cidade nova", PosicaoGravada.Ausente, "100", "202602", true),
            new LocalidadeGravada(saoPaulo, "SP", "Sé (centro)", "se", PosicaoGravada.Valida(-23.55m, -46.63m), "200", "202602", true));
    }

    private async Task<List<LocalidadeGravada>> LerDistritosAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        List<Distrito> distritos = await leitura.Distritos.ToListAsync();
        return [.. distritos.Select(d => LocalidadeGravada.De(d)).OrderBy(d => d.NomeNormalizado, StringComparer.Ordinal)];
    }

    private async Task<List<LocalidadeGravada>> LerBairrosAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        List<Bairro> bairros = await leitura.Bairros.ToListAsync();
        return [.. bairros.Select(b => LocalidadeGravada.De(b)).OrderBy(b => b.NomeNormalizado, StringComparer.Ordinal)];
    }

    private sealed record LocalidadeGravada(
        Guid CidadeId,
        string Uf,
        string Nome,
        string NomeNormalizado,
        PosicaoGravada Posicao,
        string? IdOrigemDne,
        string Versao,
        bool Vigente)
    {
        public static LocalidadeGravada De(Distrito d) =>
            new(
                d.CidadeId, d.Uf, d.Nome, d.NomeNormalizado,
                PosicaoGravada.De(d.Latitude, d.Longitude, d.Coordenada),
                d.IdOrigemDne, d.VersaoDataset, d.Vigente);

        public static LocalidadeGravada De(Bairro b) =>
            new(
                b.CidadeId, b.Uf, b.Nome, b.NomeNormalizado,
                PosicaoGravada.De(b.Latitude, b.Longitude, b.Coordenada),
                b.IdOrigemDne, b.VersaoDataset, b.Vigente);
    }
}
