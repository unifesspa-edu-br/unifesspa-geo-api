namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Fixa o que a carga grava nas nove métricas, no gentílico, no responsável e na proveniência dos
/// indicadores do estado e da cidade (exceto o identificador e os instantes): métrica ausente ou
/// inválida vira nula; a segunda carga troca os valores e a fonte com chave repetida fica com a última.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ValoresGravadosDosIndicadoresTests
{
    private const string CodigoDeMaraba = "1500402";
    private const string CodigoDeSaoPaulo = "3550308";

    private readonly GeoPostgisFixture _fixture;
    private readonly CargaDosValoresGravados _carga;

    public ValoresGravadosDosIndicadoresTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _carga = new CargaDosValoresGravados(fixture);
    }

    [Fact(DisplayName = "Primeira carga: o indicador do estado grava as nove métricas e métrica ausente ou inválida vira nula")]
    public async Task PrimeiraCarga_GravaOsIndicadoresDosEstados()
    {
        await _carga.CarregarAPrimeiraAsync();

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await LerDoEstadoAsync(leitura, "PA")).Should().Be(new IndicadorDoEstadoGravado(
            await _carga.EstadoIdAsync("PA"), "paraense", "Governador Fictício A", 1245870.704m, 8120131, 6.52m, 1000000,
            0.646m, 50000.50m, 40000.25m, 1200, 2500000, "202601", true));
        (await LerDoEstadoAsync(leitura, "SP")).Should().Be(new IndicadorDoEstadoGravado(
            await _carga.EstadoIdAsync("SP"), null, null, null, null, null, null,
            0.826m, null, null, null, 30000000, "202601", true));
    }

    [Fact(DisplayName = "Primeira carga: o indicador da cidade grava as nove métricas e métrica ausente ou inválida vira nula")]
    public async Task PrimeiraCarga_GravaOsIndicadoresDasCidades()
    {
        await _carga.CarregarAPrimeiraAsync();

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await LerDaCidadeAsync(leitura, CodigoDeMaraba)).Should().Be(new IndicadorDaCidadeGravado(
            await _carga.CidadeIdAsync(CodigoDeMaraba), "marabaense", "Prefeito Fictício A", 15128.058m, 266533, 17.62m, 97.5m,
            0.668m, 14.45m, 800000000.25m, 750000000.10m, 30000.99m, "05/04", "202601", true));
        (await LerDaCidadeAsync(leitura, CodigoDeSaoPaulo)).Should().Be(new IndicadorDaCidadeGravado(
            await _carga.CidadeIdAsync(CodigoDeSaoPaulo), null, null, 1521.11m, null, 7528.26m, null,
            0.805m, null, null, null, null, null, "202601", true));
    }

    [Fact(DisplayName = "Segunda carga: os indicadores trocam os valores e mantêm o identificador")]
    public async Task SegundaCarga_AtualizaOsIndicadores()
    {
        await _carga.CarregarAPrimeiraAsync();
        Guid estadoAntes = await IdDoIndicadorDoEstadoAsync("PA");
        Guid cidadeAntes = await IdDoIndicadorDaCidadeAsync(CodigoDeMaraba);

        await _carga.CarregarASegundaAsync();

        await ConferirOsValoresDaSegundaAsync();
        (await IdDoIndicadorDoEstadoAsync("PA")).Should().Be(estadoAntes);
        (await IdDoIndicadorDaCidadeAsync(CodigoDeMaraba)).Should().Be(cidadeAntes);
    }

    [Fact(DisplayName = "Chave repetida na fonte: a última ocorrência de cada indicador vale")]
    public async Task ChaveRepetida_UltimaOcorrenciaVale()
    {
        await _carga.CarregarComChaveRepetidaAsync();

        await ConferirOsValoresDaSegundaAsync();
    }

    private static async Task<IndicadorDoEstadoGravado> LerDoEstadoAsync(GeoDbContext leitura, string uf) =>
        IndicadorDoEstadoGravado.De(await leitura.EstadoIndicadores.SingleAsync(i => leitura.Estados.Any(e => e.Id == i.EstadoId && e.Uf == uf)));

    private static async Task<IndicadorDaCidadeGravado> LerDaCidadeAsync(GeoDbContext leitura, string codigoIbge) =>
        IndicadorDaCidadeGravado.De(await leitura.CidadeIndicadores.SingleAsync(i => leitura.Cidades.Any(c => c.Id == i.CidadeId && c.CodigoIbge == codigoIbge)));

    private async Task<Guid> IdDoIndicadorDoEstadoAsync(string uf)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return (await leitura.EstadoIndicadores.SingleAsync(i => leitura.Estados.Any(e => e.Id == i.EstadoId && e.Uf == uf))).Id;
    }

    private async Task<Guid> IdDoIndicadorDaCidadeAsync(string codigoIbge)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return (await leitura.CidadeIndicadores.SingleAsync(i => leitura.Cidades.Any(c => c.Id == i.CidadeId && c.CodigoIbge == codigoIbge))).Id;
    }

    private async Task ConferirOsValoresDaSegundaAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.EstadoIndicadores.CountAsync()).Should().Be(2);
        (await leitura.CidadeIndicadores.CountAsync()).Should().Be(2);
        await ConferirOsIndicadoresDosEstadosDaSegundaAsync(leitura);
        await ConferirOsIndicadoresDasCidadesDaSegundaAsync(leitura);
    }

    private async Task ConferirOsIndicadoresDosEstadosDaSegundaAsync(GeoDbContext leitura)
    {
        (await LerDoEstadoAsync(leitura, "PA")).Should().Be(new IndicadorDoEstadoGravado(
            await _carga.EstadoIdAsync("PA"), "paraense (2)", "Governador Fictício B", 1245870.705m, 8120132, 6.53m, 1000001,
            0.647m, 50001.51m, 40001.26m, 1201, 2500001, "202602", true));
        (await LerDoEstadoAsync(leitura, "SP")).Should().Be(new IndicadorDoEstadoGravado(
            await _carga.EstadoIdAsync("SP"), "paulista", "Governador Fictício C", 248219.481m, 44411238, 178.9m, 5000000,
            0.806m, 900000.99m, 800000.88m, 2500, 30000001, "202602", true));
    }

    private async Task ConferirOsIndicadoresDasCidadesDaSegundaAsync(GeoDbContext leitura)
    {
        (await LerDaCidadeAsync(leitura, CodigoDeMaraba)).Should().Be(new IndicadorDaCidadeGravado(
            await _carga.CidadeIdAsync(CodigoDeMaraba), "marabaense (2)", "Prefeito Fictício B", 15128.059m, 266534, 17.63m, 97.6m,
            0.669m, 14.46m, 800000001.26m, 750000001.11m, 30001.00m, "06/04", "202602", true));
        (await LerDaCidadeAsync(leitura, CodigoDeSaoPaulo)).Should().Be(new IndicadorDaCidadeGravado(
            await _carga.CidadeIdAsync(CodigoDeSaoPaulo), "paulistano", "Prefeito Fictício C", 1521.12m, 11451999, 7528.27m, 98.1m,
            0.806m, 10.2m, 9000000000.50m, 8000000000.25m, 50000.12m, "25/01", "202602", true));
    }

    private sealed record IndicadorDoEstadoGravado(
        Guid EstadoId,
        string? Gentilico,
        string? Governador,
        decimal? AreaKm2,
        int? PopulacaoResidente2022,
        decimal? DensidadeDemografica,
        int? MatriculasEnsinoFundamental2023,
        decimal? Idh,
        decimal? ReceitasBrutas,
        decimal? DespesasBrutas,
        int? RendimentoMensalPerCapita,
        int? TotalVeiculos2023,
        string Versao,
        bool Vigente)
    {
        public static IndicadorDoEstadoGravado De(EstadoIndicador i) =>
            new(
                i.EstadoId, i.Gentilico, i.Governador, i.AreaKm2, i.PopulacaoResidente2022, i.DensidadeDemografica,
                i.MatriculasEnsinoFundamental2023, i.Idh, i.ReceitasBrutas, i.DespesasBrutas,
                i.RendimentoMensalPerCapita, i.TotalVeiculos2023, i.VersaoDataset, i.Vigente);
    }

    private sealed record IndicadorDaCidadeGravado(
        Guid CidadeId,
        string? Gentilico,
        string? Prefeito,
        decimal? AreaKm2,
        int? PopulacaoResidente,
        decimal? DensidadeDemografica,
        decimal? Escolarizacao6a14,
        decimal? Idh,
        decimal? MortalidadeInfantil,
        decimal? Receitas,
        decimal? Despesas,
        decimal? PibPerCapita,
        string? Aniversario,
        string Versao,
        bool Vigente)
    {
        public static IndicadorDaCidadeGravado De(CidadeIndicador i) =>
            new(
                i.CidadeId, i.Gentilico, i.Prefeito, i.AreaKm2, i.PopulacaoResidente, i.DensidadeDemografica,
                i.Escolarizacao6a14, i.Idh, i.MortalidadeInfantil, i.Receitas, i.Despesas, i.PibPerCapita,
                i.Aniversario, i.VersaoDataset, i.Vigente);
    }
}
