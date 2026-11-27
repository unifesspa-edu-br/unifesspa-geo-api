namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Fixa o que a carga grava em cada coluna das faixas de CEP do estado, da cidade, do distrito e do
/// bairro (exceto o identificador e os instantes): CEP aparado, descrição em branco vira nula, a
/// segunda carga atualiza descrição e versão e a fonte com chave repetida fica com a última linha.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ValoresGravadosDasFaixasDeCepTests
{
    private const string CodigoDeMaraba = "1500402";
    private const string CodigoDeSaoPaulo = "3550308";

    private readonly GeoPostgisFixture _fixture;
    private readonly CargaDosValoresGravados _carga;

    public ValoresGravadosDasFaixasDeCepTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _carga = new CargaDosValoresGravados(fixture);
    }

    [Fact(DisplayName = "Primeira carga: as faixas do estado gravam CEP aparado e descrição, e descrição em branco vira nula")]
    public async Task PrimeiraCarga_GravaAsFaixasDoEstado()
    {
        await _carga.CarregarAPrimeiraAsync();

        await ConferirFaixasDoEstadoAsync("202601", "Capital", "Interior", null);
    }

    [Fact(DisplayName = "Primeira carga: as faixas da cidade, do distrito e do bairro gravam a localidade, o CEP aparado e a versão")]
    public async Task PrimeiraCarga_GravaAsFaixasDasLocalidades()
    {
        await _carga.CarregarAPrimeiraAsync();

        await ConferirFaixasDasLocalidadesAsync("202601");
    }

    [Fact(DisplayName = "Segunda carga: as faixas do estado trocam a descrição, e as demais gravam a nova versão, todas com o mesmo identificador")]
    public async Task SegundaCarga_AtualizaAsFaixas()
    {
        await _carga.CarregarAPrimeiraAsync();
        List<Guid> antes = await IdentificadoresDasFaixasAsync();

        await _carga.CarregarASegundaAsync();

        await ConferirFaixasDoEstadoAsync("202602", "Capital (Belém)", "Interior do Pará", "Capital paulista");
        await ConferirFaixasDasLocalidadesAsync("202602");
        (await IdentificadoresDasFaixasAsync()).Should().BeEquivalentTo(antes);
    }

    [Fact(DisplayName = "Chave repetida na fonte: a última ocorrência de cada faixa vale e não há faixa duplicada")]
    public async Task ChaveRepetida_UltimaOcorrenciaVale()
    {
        await _carga.CarregarComChaveRepetidaAsync();

        await ConferirFaixasDoEstadoAsync("202602", "Capital (Belém)", "Interior do Pará", "Capital paulista");
        await ConferirFaixasDasLocalidadesAsync("202602");
    }

    private async Task ConferirFaixasDoEstadoAsync(string versao, string capitalDoPara, string interiorDoPara, string? capitalPaulista)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Guid paraId = await _carga.EstadoIdAsync("PA");
        Guid saoPauloId = await _carga.EstadoIdAsync("SP");

        List<EstadoFaixaCep> faixas = await leitura.EstadoFaixasCep.ToListAsync();

        faixas.Select(f => FaixaDoEstadoGravada.De(f)).OrderBy(f => f.CepInicial, StringComparer.Ordinal).Should().Equal(
            new FaixaDoEstadoGravada(saoPauloId, "01000000", "05999999", capitalPaulista, versao, true),
            new FaixaDoEstadoGravada(paraId, "66000000", "66999999", capitalDoPara, versao, true),
            new FaixaDoEstadoGravada(paraId, "67000000", "68899999", interiorDoPara, versao, true));
    }

    private async Task ConferirFaixasDasLocalidadesAsync(string versao)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Guid maraba = await _carga.CidadeIdAsync(CodigoDeMaraba);
        Guid saoPaulo = await _carga.CidadeIdAsync(CodigoDeSaoPaulo);

        (await leitura.CidadeFaixasCep.ToListAsync()).Select(f => FaixaGravada.De(f)).OrderBy(f => f.CepInicial, StringComparer.Ordinal).Should().Equal(
            new FaixaGravada(saoPaulo, "01000000", "05999999", versao, true),
            new FaixaGravada(maraba, "68500000", "68519999", versao, true));
        await ConferirFaixasDosDistritosAsync(leitura, versao);
        await ConferirFaixasDosBairrosAsync(leitura, versao);
    }

    private async Task ConferirFaixasDosDistritosAsync(GeoDbContext leitura, string versao)
    {
        Guid centro = await _carga.DistritoIdAsync("centro");
        Guid cidadeNova = await _carga.DistritoIdAsync("cidade nova");

        (await leitura.DistritoFaixasCep.ToListAsync()).Select(f => FaixaGravada.De(f)).OrderBy(f => f.CepInicial, StringComparer.Ordinal).Should().Equal(
            new FaixaGravada(centro, "01000000", "01099999", versao, true),
            new FaixaGravada(cidadeNova, "68500000", "68519999", versao, true));
    }

    private async Task ConferirFaixasDosBairrosAsync(GeoDbContext leitura, string versao)
    {
        Guid se = await _carga.BairroIdAsync("se");
        Guid cidadeNova = await _carga.BairroIdAsync("cidade nova");

        (await leitura.BairroFaixasCep.ToListAsync()).Select(f => FaixaGravada.De(f)).OrderBy(f => f.CepInicial, StringComparer.Ordinal).Should().Equal(
            new FaixaGravada(se, "01000000", "01049999", versao, true),
            new FaixaGravada(cidadeNova, "68500000", "68509999", versao, true));
    }

    private async Task<List<Guid>> IdentificadoresDasFaixasAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        List<Guid> ids = await leitura.EstadoFaixasCep.Select(f => f.Id).ToListAsync();
        ids.AddRange(await leitura.CidadeFaixasCep.Select(f => f.Id).ToListAsync());
        ids.AddRange(await leitura.DistritoFaixasCep.Select(f => f.Id).ToListAsync());
        ids.AddRange(await leitura.BairroFaixasCep.Select(f => f.Id).ToListAsync());
        return ids;
    }

    private sealed record FaixaDoEstadoGravada(Guid EstadoId, string CepInicial, string CepFinal, string? Descricao, string Versao, bool Vigente)
    {
        public static FaixaDoEstadoGravada De(EstadoFaixaCep f) =>
            new(f.EstadoId, f.CepInicial, f.CepFinal, f.Descricao, f.VersaoDataset, f.Vigente);
    }

    private sealed record FaixaGravada(Guid LocalidadeId, string CepInicial, string CepFinal, string Versao, bool Vigente)
    {
        public static FaixaGravada De(CidadeFaixaCep f) => new(f.CidadeId, f.CepInicial, f.CepFinal, f.VersaoDataset, f.Vigente);

        public static FaixaGravada De(DistritoFaixaCep f) => new(f.DistritoId, f.CepInicial, f.CepFinal, f.VersaoDataset, f.Vigente);

        public static FaixaGravada De(BairroFaixaCep f) => new(f.BairroId, f.CepInicial, f.CepFinal, f.VersaoDataset, f.Vigente);
    }
}
