namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Fixa o que a carga grava em cada coluna da cidade (exceto o identificador e os instantes de
/// criação e atualização), inclusive o nome oficial do IBGE, a forma de busca derivada do nome,
/// a coordenada e a divisão regional: primeira carga, segunda sobre as mesmas chaves e chave repetida.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ValoresGravadosDaCidadeTests
{
    private const string CodigoDeMaraba = "1500402";
    private const string CodigoDeSaoPaulo = "3550308";

    private static readonly RegiaoGravada SemRegiao = new(null, null);

    private readonly GeoPostgisFixture _fixture;
    private readonly CargaDosValoresGravados _carga;

    public ValoresGravadosDaCidadeTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _carga = new CargaDosValoresGravados(fixture);
    }

    [Fact(DisplayName = "Primeira carga: a cidade grava o nome da fonte aparado, a forma de busca sem acento e a divisão regional")]
    public async Task PrimeiraCarga_GravaACidadeComNomeDaFonte()
    {
        await _carga.CarregarAPrimeiraAsync();

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        CidadeGravada.De(await LerAsync(leitura, CodigoDeMaraba)).Should().Be(new CidadeGravada(
            await _carga.EstadoIdAsync("PA"), "PA", CodigoDeMaraba, "Marabá D'Oeste", "maraba doeste", "maraba doeste", "94",
            PosicaoGravada.Valida(-5.36867m, -49.11731m),
            new RegiaoGravada("1506", "Sudeste Paraense"), new RegiaoGravada("15021", "Marabá"),
            new RegiaoGravada("1502", "Marabá"), new RegiaoGravada("150003", null), "202601", true));
    }

    [Fact(DisplayName = "Primeira carga: o nome oficial do IBGE prevalece, a coordenada fora do domínio deixa a posição vazia e sem território tudo é nulo")]
    public async Task PrimeiraCarga_GravaACidadeComNomeOficial()
    {
        await _carga.CarregarAPrimeiraAsync();

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        // Latitude -95.5 e longitude 200.25 estão fora do domínio: a posição inteira fica vazia.
        CidadeGravada.De(await LerAsync(leitura, CodigoDeSaoPaulo)).Should().Be(new CidadeGravada(
            await _carga.EstadoIdAsync("SP"), "SP", CodigoDeSaoPaulo, "São Paulo", "sao paulo", "sao paulo", "11",
            PosicaoGravada.Ausente, SemRegiao, SemRegiao, SemRegiao, SemRegiao, "202601", true));
    }

    [Fact(DisplayName = "Segunda carga: a cidade troca os valores, mantém o identificador e o território em branco vira nulo")]
    public async Task SegundaCarga_AtualizaAsCidades()
    {
        await _carga.CarregarAPrimeiraAsync();
        Guid cidadeAntes = await _carga.CidadeIdAsync(CodigoDeMaraba);

        await _carga.CarregarASegundaAsync();

        await ConferirOsValoresDaSegundaAsync();
        (await _carga.CidadeIdAsync(CodigoDeMaraba)).Should().Be(cidadeAntes);
    }

    [Fact(DisplayName = "Chave repetida na fonte: a última ocorrência da cidade vale")]
    public async Task ChaveRepetida_UltimaOcorrenciaVale()
    {
        await _carga.CarregarComChaveRepetidaAsync();

        await ConferirOsValoresDaSegundaAsync();
    }

    private static Task<Cidade> LerAsync(GeoDbContext leitura, string codigoIbge) =>
        leitura.Cidades.SingleAsync(c => c.CodigoIbge == codigoIbge);

    private async Task ConferirOsValoresDaSegundaAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.Cidades.CountAsync()).Should().Be(2);
        // Latitude 91 e longitude -181 estão fora do domínio: a posição inteira fica vazia.
        CidadeGravada.De(await LerAsync(leitura, CodigoDeMaraba)).Should().Be(new CidadeGravada(
            await _carga.EstadoIdAsync("PA"), "PA", CodigoDeMaraba, "Marabá Nova", "maraba nova", "maraba nova", "91",
            PosicaoGravada.Ausente, SemRegiao, SemRegiao, SemRegiao, SemRegiao, "202602", true));
        CidadeGravada.De(await LerAsync(leitura, CodigoDeSaoPaulo)).Should().Be(new CidadeGravada(
            await _carga.EstadoIdAsync("SP"), "SP", CodigoDeSaoPaulo, "São Paulo", "sao paulo", "sao paulo", null,
            PosicaoGravada.Valida(-23.55m, -46.63m),
            new RegiaoGravada("3515", "Macrometropolitana Paulista"), new RegiaoGravada("35061", "São Paulo"),
            new RegiaoGravada("3501", "São Paulo"), new RegiaoGravada("350001", "São Paulo"), "202602", true));
    }

    private sealed record RegiaoGravada(string? Codigo, string? Nome);

    private sealed record CidadeGravada(
        Guid EstadoId,
        string Uf,
        string CodigoIbge,
        string Nome,
        string? NomeNormalizado,
        string NomeOrdenacao,
        string? Ddd,
        PosicaoGravada Posicao,
        RegiaoGravada Mesorregiao,
        RegiaoGravada Microrregiao,
        RegiaoGravada RegiaoIntermediaria,
        RegiaoGravada RegiaoImediata,
        string Versao,
        bool Vigente)
    {
        public static CidadeGravada De(Cidade c) =>
            new(
                c.EstadoId, c.Uf, c.CodigoIbge, c.Nome, c.NomeNormalizado, c.NomeOrdenacao, c.Ddd,
                PosicaoGravada.De(c.Latitude, c.Longitude, c.Coordenada),
                new RegiaoGravada(c.MesorregiaoCodigo, c.MesorregiaoNome),
                new RegiaoGravada(c.MicrorregiaoCodigo, c.MicrorregiaoNome),
                new RegiaoGravada(c.RegiaoIntermediariaCodigo, c.RegiaoIntermediariaNome),
                new RegiaoGravada(c.RegiaoImediataCodigo, c.RegiaoImediataNome),
                c.VersaoDataset, c.Vigente);
    }
}
