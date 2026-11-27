namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Fixa o que a carga grava em cada coluna do CEP de grande usuário e do CEP geral (exceto o
/// identificador e os instantes): CEP aparado, forma de busca opcional, cidade e distrito
/// resolvidos, coordenada validada, segunda carga e chave repetida.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ValoresGravadosDosCepsTests
{
    private const string CodigoDeMaraba = "1500402";
    private const string CodigoDeSaoPaulo = "3550308";

    private readonly GeoPostgisFixture _fixture;
    private readonly CargaDosValoresGravados _carga;

    public ValoresGravadosDosCepsTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _carga = new CargaDosValoresGravados(fixture);
    }

    [Fact(DisplayName = "Primeira carga: o CEP de grande usuário grava nome aparado e forma de busca em minúsculas, ou nula se em branco")]
    public async Task PrimeiraCarga_GravaOsGrandesUsuarios()
    {
        await _carga.CarregarAPrimeiraAsync();

        (await LerGrandesUsuariosAsync()).Should().Equal(
            new GrandeUsuarioGravado("01000900", "Câmara Municipal", null, "202601", true),
            new GrandeUsuarioGravado("68500900", "Prefeitura de Marabá", "prefeitura de maraba", "202601", true));
    }

    [Fact(DisplayName = "Primeira carga: o CEP geral grava cidade, distrito, UF e a coordenada válida ou ausente")]
    public async Task PrimeiraCarga_GravaOsCepsGerais()
    {
        await _carga.CarregarAPrimeiraAsync();

        Guid maraba = await _carga.CidadeIdAsync(CodigoDeMaraba);
        Guid saoPaulo = await _carga.CidadeIdAsync(CodigoDeSaoPaulo);
        (await LerCepsGeraisAsync()).Should().Equal(
            new CepGeralGravado("68525000", maraba, null, "PA", PosicaoGravada.Valida(-5.046m, -48.59947m), "202601", true),
            new CepGeralGravado("68525100", maraba, await _carga.DistritoIdAsync("cidade nova"), "PA", PosicaoGravada.Ausente, "202601", true),
            new CepGeralGravado("68525200", saoPaulo, null, "SP", PosicaoGravada.Ausente, "202601", true));
    }

    [Fact(DisplayName = "Segunda carga: os CEPs trocam os valores, mantêm a chave e o identificador e gravam a nova versão")]
    public async Task SegundaCarga_AtualizaOsCeps()
    {
        await _carga.CarregarAPrimeiraAsync();
        Guid antes = (await LerCepsGeraisIdentificadosAsync())["68525000"];

        await _carga.CarregarASegundaAsync();

        await ConferirOsValoresDaSegundaAsync();
        (await LerCepsGeraisIdentificadosAsync())["68525000"].Should().Be(antes);
    }

    [Fact(DisplayName = "Chave repetida na fonte: a última ocorrência de cada CEP vale")]
    public async Task ChaveRepetida_UltimaOcorrenciaVale()
    {
        await _carga.CarregarComChaveRepetidaAsync();

        await ConferirOsValoresDaSegundaAsync();
    }

    private async Task ConferirOsValoresDaSegundaAsync()
    {
        Guid maraba = await _carga.CidadeIdAsync(CodigoDeMaraba);
        Guid saoPaulo = await _carga.CidadeIdAsync(CodigoDeSaoPaulo);
        (await LerGrandesUsuariosAsync()).Should().Equal(
            new GrandeUsuarioGravado("01000900", "Câmara Municipal de São Paulo", "camara municipal de sao paulo", "202602", true),
            new GrandeUsuarioGravado("68500900", "Prefeitura Municipal de Marabá", null, "202602", true));
        (await LerCepsGeraisAsync()).Should().Equal(
            new CepGeralGravado("68525000", saoPaulo, await _carga.DistritoIdAsync("centro"), "SP", PosicaoGravada.Valida(-23.5m, -46.6m), "202602", true),
            new CepGeralGravado("68525100", maraba, null, "PA", PosicaoGravada.Valida(-5.05m, -48.6m), "202602", true),
            new CepGeralGravado("68525200", maraba, await _carga.DistritoIdAsync("cidade nova"), "PA", PosicaoGravada.Ausente, "202602", true));
    }

    private async Task<List<GrandeUsuarioGravado>> LerGrandesUsuariosAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        List<CepGrandeUsuario> ceps = await leitura.CepGrandesUsuarios.ToListAsync();
        return [.. ceps.Select(c => GrandeUsuarioGravado.De(c)).OrderBy(c => c.Cep, StringComparer.Ordinal)];
    }

    private async Task<List<CepGeralGravado>> LerCepsGeraisAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        List<CepGeral> ceps = await leitura.CepGerais.ToListAsync();
        return [.. ceps.Select(c => CepGeralGravado.De(c)).OrderBy(c => c.Cep, StringComparer.Ordinal)];
    }

    private async Task<Dictionary<string, Guid>> LerCepsGeraisIdentificadosAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return await leitura.CepGerais.ToDictionaryAsync(c => c.Cep, c => c.Id, StringComparer.Ordinal);
    }

    private sealed record GrandeUsuarioGravado(string Cep, string Nome, string? NomeNormalizado, string Versao, bool Vigente)
    {
        public static GrandeUsuarioGravado De(CepGrandeUsuario c) =>
            new(c.Cep, c.Nome, c.NomeNormalizado, c.VersaoDataset, c.Vigente);
    }

    private sealed record CepGeralGravado(
        string Cep,
        Guid CidadeId,
        Guid? DistritoId,
        string Uf,
        PosicaoGravada Posicao,
        string Versao,
        bool Vigente)
    {
        public static CepGeralGravado De(CepGeral c) =>
            new(
                c.Cep, c.CidadeId, c.DistritoId, c.Uf,
                PosicaoGravada.De(c.Latitude, c.Longitude, c.Coordenada),
                c.VersaoDataset, c.Vigente);
    }
}
