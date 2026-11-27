namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Fixa o que a carga grava em cada coluna do país e dos estados (exceto o identificador e os
/// instantes de criação e atualização): a primeira carga, a segunda sobre as mesmas chaves e a
/// fonte com chave repetida, em que a última ocorrência vale.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ValoresGravadosDoPaisEDoEstadoTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CargaDosValoresGravados _carga;

    public ValoresGravadosDoPaisEDoEstadoTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _carga = new CargaDosValoresGravados(fixture);
    }

    [Fact(DisplayName = "Primeira carga: o país grava as siglas em maiúsculas, o nome aparado e os códigos dos cadastros federais")]
    public async Task PrimeiraCarga_GravaOPais()
    {
        await _carga.CarregarAPrimeiraAsync();

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        PaisGravado.De(await leitura.Paises.SingleAsync())
            .Should().Be(new PaisGravado("BRA", "BR", "Brasil", "1058", null, "0105", null, "202601", true));
    }

    [Fact(DisplayName = "Primeira carga: o estado grava UF, nome, forma de busca, região, capital, código IBGE, coordenada e faixa geral")]
    public async Task PrimeiraCarga_GravaOsEstados()
    {
        await _carga.CarregarAPrimeiraAsync();

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Guid paisId = await _carga.PaisIdAsync();
        EstadoGravado.De(await leitura.Estados.SingleAsync(e => e.Uf == "PA")).Should().Be(new EstadoGravado(
            paisId, "PA", "Pará", "para", "para", "Norte", "Belém", "15",
            PosicaoGravada.Valida(-5.36867m, -49.11731m), "66000000", "68899999", "202601", true));
        EstadoGravado.De(await leitura.Estados.SingleAsync(e => e.Uf == "SP")).Should().Be(new EstadoGravado(
            paisId, "SP", "São Paulo", null, "sao paulo", null, null, null,
            PosicaoGravada.SoNasColunas(95.5m, -200m), null, null, "202601", true));
    }

    [Fact(DisplayName = "Segunda carga: o país e os estados trocam os valores, mantêm o identificador e gravam a nova versão")]
    public async Task SegundaCarga_AtualizaOPaisEOsEstados()
    {
        await _carga.CarregarAPrimeiraAsync();
        Guid paisAntes = await _carga.PaisIdAsync();
        Guid estadoAntes = await _carga.EstadoIdAsync("PA");

        await _carga.CarregarASegundaAsync();

        await ConferirOsValoresDaSegundaAsync();
        (await _carga.PaisIdAsync()).Should().Be(paisAntes);
        (await _carga.EstadoIdAsync("PA")).Should().Be(estadoAntes);
    }

    [Fact(DisplayName = "Chave repetida na fonte: a última ocorrência do país e dos estados vale")]
    public async Task ChaveRepetida_UltimaOcorrenciaVale()
    {
        await _carga.CarregarComChaveRepetidaAsync();

        await ConferirOsValoresDaSegundaAsync();
    }

    private async Task ConferirOsValoresDaSegundaAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        PaisGravado.De(await leitura.Paises.SingleAsync()).Should().Be(
            new PaisGravado("BRA", "BZ", "República Federativa do Brasil", null, "2058", "3058", "4058", "202602", true));
        (await leitura.Estados.CountAsync()).Should().Be(2);
        await ConferirOsEstadosDaSegundaAsync(leitura);
    }

    private async Task ConferirOsEstadosDaSegundaAsync(GeoDbContext leitura)
    {
        Guid paisId = await _carga.PaisIdAsync();
        EstadoGravado.De(await leitura.Estados.SingleAsync(e => e.Uf == "PA")).Should().Be(new EstadoGravado(
            paisId, "PA", "Estado do Pará", "estado do para", "estado do para", null, "Belém do Pará", "16",
            PosicaoGravada.Valida(-3.9m, -52.5m), "66000001", "68899998", "202602", true));
        EstadoGravado.De(await leitura.Estados.SingleAsync(e => e.Uf == "SP")).Should().Be(new EstadoGravado(
            paisId, "SP", "São Paulo", "sao paulo estado", "sao paulo estado", "Sudeste", "São Paulo", "35",
            PosicaoGravada.Valida(-23.55m, -46.63m), "01000000", "19999999", "202602", true));
    }

    private sealed record PaisGravado(
        string SiglaIso,
        string Sigla,
        string Nome,
        string? CodigoBcb,
        string? CodigoRfb,
        string? CodigoSped,
        string? CodigoSiscomex,
        string Versao,
        bool Vigente)
    {
        public static PaisGravado De(Pais pais) =>
            new(
                pais.SiglaIso, pais.Sigla, pais.Nome,
                pais.CodigoBcb, pais.CodigoRfb, pais.CodigoSped, pais.CodigoSiscomex,
                pais.VersaoDataset, pais.Vigente);
    }

    private sealed record EstadoGravado(
        Guid PaisId,
        string Uf,
        string Nome,
        string? NomeNormalizado,
        string NomeOrdenacao,
        string? Regiao,
        string? Capital,
        string? CodigoIbge,
        PosicaoGravada Posicao,
        string? CepInicial,
        string? CepFinal,
        string Versao,
        bool Vigente)
    {
        public static EstadoGravado De(Estado estado) =>
            new(
                estado.PaisId, estado.Uf, estado.Nome, estado.NomeNormalizado, estado.NomeOrdenacao,
                estado.Regiao, estado.Capital, estado.CodigoIbge,
                PosicaoGravada.De(estado.Latitude, estado.Longitude, estado.Coordenada),
                estado.CepInicial, estado.CepFinal, estado.VersaoDataset, estado.Vigente);
    }
}
