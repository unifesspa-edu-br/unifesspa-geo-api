namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Fixa o que a carga em lote grava em cada coluna do logradouro e do complemento (exceto o
/// identificador e os instantes): textos aparados, forma de busca em minúsculas que compõe a chave,
/// UF em maiúsculas, indicador de CEP ativo, localidades resolvidas, coordenada validada, segunda
/// carga sobre as mesmas chaves e fonte com chave repetida, em que a última ocorrência vale.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ValoresGravadosDosLogradourosTests
{
    private const string CodigoDeMaraba = "1500402";
    private const string CodigoDeSaoPaulo = "3550308";

    private readonly GeoPostgisFixture _fixture;
    private readonly CargaDosValoresGravados _carga;

    public ValoresGravadosDosLogradourosTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _carga = new CargaDosValoresGravados(fixture);
    }

    [Fact(DisplayName = "Primeira carga: o logradouro grava tipo, nomes, forma de busca, localidades, UF, coordenada e indicador de ativo")]
    public async Task PrimeiraCarga_GravaOsLogradouros()
    {
        await _carga.CarregarAPrimeiraAsync();

        (await LerLogradourosAsync()).Should().Equal(
            await PracaDaSeDaPrimeiraAsync(), await RuaADaPrimeiraAsync(), await RuaBDaPrimeiraAsync());
    }

    [Fact(DisplayName = "Primeira carga: o complemento grava CEP aparado, texto aparado e forma de busca, que cai para o próprio texto se faltar")]
    public async Task PrimeiraCarga_GravaOsComplementos()
    {
        await _carga.CarregarAPrimeiraAsync();

        (await LerComplementosAsync()).Should().Equal(
            new ComplementoGravado("01001000", "Sem Texto Normalizado", "sem texto normalizado", "202601", true),
            new ComplementoGravado("68500000", "Lado Ímpar", "lado impar", "202601", true),
            new ComplementoGravado("68500000", "lado par", "lado par", "202601", true));
    }

    [Fact(DisplayName = "Segunda carga: o logradouro e o complemento trocam os valores, mantêm a chave e o identificador e gravam a nova versão")]
    public async Task SegundaCarga_AtualizaOsLogradourosEOsComplementos()
    {
        await _carga.CarregarAPrimeiraAsync();
        List<Guid> antes = await IdentificadoresAsync();

        await _carga.CarregarASegundaAsync();

        await ConferirOsValoresDaSegundaAsync();
        (await IdentificadoresAsync()).Should().BeEquivalentTo(antes);
    }

    [Fact(DisplayName = "Chave repetida na fonte: a última ocorrência de cada logradouro e de cada complemento vale")]
    public async Task ChaveRepetida_UltimaOcorrenciaVale()
    {
        await _carga.CarregarComChaveRepetidaAsync();

        await ConferirOsValoresDaSegundaAsync();
    }

    private async Task ConferirOsValoresDaSegundaAsync()
    {
        Guid maraba = await _carga.CidadeIdAsync(CodigoDeMaraba);
        Guid saoPaulo = await _carga.CidadeIdAsync(CodigoDeSaoPaulo);
        (await LerLogradourosAsync()).Should().Equal(
            new LogradouroGravado(
                "01001000", "Praça", "da Sé", "Praça da Sé", "praca da se", saoPaulo, null, null, "SP",
                PosicaoGravada.Ausente, true, "202602", true),
            new LogradouroGravado(
                "68500000", "Travessa", "A Nova", "Travessa A Nova", "rua a", maraba, null, null, "PA",
                PosicaoGravada.Valida(-5.37m, -49.12m), false, "202602", true),
            await RuaBDaSegundaAsync());
        await ConferirOsComplementosDaSegundaAsync();
    }

    private async Task ConferirOsComplementosDaSegundaAsync() =>
        (await LerComplementosAsync()).Should().Equal(
            new ComplementoGravado("01001000", "SEM Texto Normalizado", "sem texto normalizado", "202602", true),
            new ComplementoGravado("68500000", "Lado ímpar revisto", "lado impar", "202602", true),
            new ComplementoGravado("68500000", "Lado Par (revisado)", "lado par", "202602", true));

    private async Task<LogradouroGravado> PracaDaSeDaPrimeiraAsync() =>
        new(
            "01001000", "Praça", "da Sé", "Praça da Sé", "praca da se", await _carga.CidadeIdAsync(CodigoDeSaoPaulo),
            await _carga.DistritoIdAsync("centro"), await _carga.BairroIdAsync("se"), "SP",
            PosicaoGravada.Valida(-23.55m, -46.63m), true, "202601", true);

    private async Task<LogradouroGravado> RuaADaPrimeiraAsync() =>
        new(
            "68500000", "Rua", "A", "Rua A", "rua a", await _carga.CidadeIdAsync(CodigoDeMaraba),
            await _carga.DistritoIdAsync("cidade nova"), await _carga.BairroIdAsync("cidade nova"), "PA",
            PosicaoGravada.Valida(-5.36867m, -49.11731m), true, "202601", true);

    private async Task<LogradouroGravado> RuaBDaPrimeiraAsync() =>
        new(
            "68500001", null, "Rua B", null, "rua b", await _carga.CidadeIdAsync(CodigoDeMaraba), null, null, "PA",
            PosicaoGravada.Ausente, false, "202601", true);

    private async Task<LogradouroGravado> RuaBDaSegundaAsync() =>
        new(
            "68500001", "Rua", "B Segunda", "Rua B Segunda", "rua b", await _carga.CidadeIdAsync(CodigoDeMaraba),
            null, await _carga.BairroIdAsync("cidade nova"), "PA",
            PosicaoGravada.Valida(-5.38m, -49.13m), true, "202602", true);

    private async Task<List<LogradouroGravado>> LerLogradourosAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        List<Logradouro> logradouros = await leitura.Logradouros.ToListAsync();
        return [.. logradouros.Select(l => LogradouroGravado.De(l)).OrderBy(l => l.Cep, StringComparer.Ordinal)];
    }

    private async Task<List<ComplementoGravado>> LerComplementosAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        List<LogradouroComplemento> complementos = await leitura.LogradouroComplementos.ToListAsync();
        return
        [
            .. complementos.Select(c => ComplementoGravado.De(c))
                .OrderBy(c => c.Cep, StringComparer.Ordinal)
                .ThenBy(c => c.ComplementoNormalizado, StringComparer.Ordinal),
        ];
    }

    private async Task<List<Guid>> IdentificadoresAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        List<Guid> ids = await leitura.Logradouros.Select(l => l.Id).ToListAsync();
        ids.AddRange(await leitura.LogradouroComplementos.Select(c => c.Id).ToListAsync());
        return ids;
    }

    private sealed record LogradouroGravado(
        string Cep,
        string? Tipo,
        string Nome,
        string? NomeCompleto,
        string NomeNormalizado,
        Guid CidadeId,
        Guid? DistritoId,
        Guid? BairroId,
        string Uf,
        PosicaoGravada Posicao,
        bool Ativo,
        string Versao,
        bool Vigente)
    {
        public static LogradouroGravado De(Logradouro l) =>
            new(
                l.Cep, l.Tipo, l.Nome, l.NomeCompleto, l.NomeNormalizado, l.CidadeId, l.DistritoId, l.BairroId, l.Uf,
                PosicaoGravada.De(l.Latitude, l.Longitude, l.Coordenada),
                l.Ativo, l.VersaoDataset, l.Vigente);
    }

    private sealed record ComplementoGravado(string Cep, string Complemento, string ComplementoNormalizado, string Versao, bool Vigente)
    {
        public static ComplementoGravado De(LogradouroComplemento c) =>
            new(c.Cep, c.Complemento, c.ComplementoNormalizado, c.VersaoDataset, c.Vigente);
    }
}
