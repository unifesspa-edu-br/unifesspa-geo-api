namespace Unifesspa.Geo.IntegrationTests;

using AwesomeAssertions;

using Infrastructure;

using Microsoft.EntityFrameworkCore;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence;

/// <summary>
/// Persistência de País/Estado/EstadoIndicador/EstadoFaixaCep contra Postgres+PostGIS
/// real (story #668). O <see cref="GeoPostgisFixture"/> é compartilhado pela coleção:
/// cada teste gera chaves naturais únicas (<see cref="GeoTestKeys"/>) para não colidir
/// nos índices UNIQUE — exceto o teste de duplicata, cuja colisão é o objeto.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class PaisEstadoPersistenceTests
{
    private const string VersaoDataset = "202601";
    private const decimal LongitudeEmGraus = -49.1278m;
    private const decimal LatitudeEmGraus = -5.3686m;

    private readonly GeoPostgisFixture _fixture;
    private readonly GravacaoNoBanco _banco;

    public PaisEstadoPersistenceTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new GravacaoNoBanco(fixture);
    }

    [Fact(DisplayName = "CA-01: País/Estado/Indicador(1:1)/Faixa persistem e relêem com Id Guid v7 e vínculos")]
    public async Task PaisEstado_PersisteELe()
    {
        CenarioDoEstadoDoPara cenario = CenarioDoEstadoDoPara.Montar();

        await _banco.GravarAsync(cenario.Entidades());

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        await ConferirPaisEEstadoAsync(leitura, cenario);
        await ConferirIndicadorEFaixaAsync(leitura, cenario);
    }

    [Fact(DisplayName = "CA-02: UF e sigla_iso duplicadas são rejeitadas pela constraint única correta")]
    public async Task ChaveNatural_RejeitaDuplicata()
    {
        await ExigirUfDuplicadaRejeitadaAsync();
        await ExigirSiglaIsoDuplicadaRejeitadaAsync();
    }

    [Fact(DisplayName = "CA-02: chave natural é case-insensitive por normalização (bra==BRA, pa==PA)")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "O teste cria deliberadamente a variante minúscula da sigla para provar que a factory a normaliza para maiúsculas.")]
    public async Task ChaveNatural_NormalizaCaixa()
    {
        string siglaBase = GeoTestKeys.SiglaIso();
        Pais maiusculo = NovoPais(siglaBase.ToUpperInvariant(), "PY", "Paraguai");
        Pais minusculo = NovoPais(siglaBase.ToLowerInvariant(), "PY", "Paraguai (caixa baixa)");

        maiusculo.SiglaIso.Should().Be(minusculo.SiglaIso, "a factory normaliza a sigla para maiúsculas");

        await _banco.GravarAsync(maiusculo);
        await _banco.ExigirViolacaoUniqueAsync(minusculo, "ix_pais_sigla_iso");
    }

    [Fact(DisplayName = "CA-03: Coordenada Point SRID 4326 persiste e relê via NetTopologySuite (geography)")]
    public async Task Estado_Coordenada_Point4326()
    {
        Pais pais = NovoPais(GeoTestKeys.SiglaIso(), "AR", "Argentina");
        Estado estado = NovoEstadoNaPosicao(pais.Id, LatitudeEmGraus, LongitudeEmGraus);

        await _banco.GravarAsync(pais, estado);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Estado lido = await leitura.Estados.SingleAsync(e => e.Id == estado.Id);

        ConferirCoordenada(lido, (double)LongitudeEmGraus, (double)LatitudeEmGraus);
    }

    [Fact(DisplayName = "CA-03: EstadoIndicador com dado ausente persiste null sem erro")]
    public async Task EstadoIndicador_DadoAusente_Null()
    {
        Pais pais = NovoPais(GeoTestKeys.SiglaIso(), "UY", "Uruguai");
        Estado estado = NovoEstado(pais.Id, GeoTestKeys.Uf(), "Rio de Janeiro");
        EstadoIndicador indicador = NovoIndicadorSemDados(estado.Id);

        await _banco.GravarAsync(pais, estado, indicador);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        EstadoIndicador lido = await leitura.EstadoIndicadores.SingleAsync(i => i.EstadoId == estado.Id);

        lido.Idh.Should().BeNull();
        lido.ReceitasBrutas.Should().BeNull();
        lido.PopulacaoResidente2022.Should().BeNull();
        lido.Vigente.Should().BeTrue();
        lido.VersaoDataset.Should().Be(VersaoDataset);
    }

    [Fact(DisplayName = "CA-05: vigente=false persiste de fato (não é mascarado pelo default DDL true)")]
    public async Task Vigente_False_Persiste()
    {
        Pais pais = NovoPais(GeoTestKeys.SiglaIso(), "PE", "Peru", vigente: false);

        await _banco.GravarAsync(pais);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Pais lido = await leitura.Paises.SingleAsync(p => p.Id == pais.Id);

        lido.Vigente.Should().BeFalse(
            "a marcação de não vigente (vigente=false) precisa chegar ao banco — não pode ser substituída pelo default true");
    }

    private static async Task ConferirPaisEEstadoAsync(GeoDbContext leitura, CenarioDoEstadoDoPara cenario)
    {
        Estado estadoLido = await leitura.Estados.SingleAsync(e => e.Id == cenario.Para.Id);
        Pais paisLido = await leitura.Paises.SingleAsync(p => p.Id == cenario.Brasil.Id);

        paisLido.Id.Version.Should().Be(7, "EntityBase usa Guid v7 (ADR-0032)");
        estadoLido.Id.Version.Should().Be(7);
        estadoLido.PaisId.Should().Be(cenario.Brasil.Id);
        estadoLido.Nome.Should().Be("Pará");
        estadoLido.Coordenada.Should().NotBeNull();
        estadoLido.Coordenada!.SRID.Should().Be(4326);
    }

    private static async Task ConferirIndicadorEFaixaAsync(GeoDbContext leitura, CenarioDoEstadoDoPara cenario)
    {
        EstadoIndicador indicadorLido = await leitura.EstadoIndicadores.SingleAsync(i => i.EstadoId == cenario.Para.Id);
        EstadoFaixaCep faixaLida = await leitura.EstadoFaixasCep.SingleAsync(f => f.Id == cenario.Faixa.Id);

        indicadorLido.EstadoId.Should().Be(cenario.Para.Id);
        indicadorLido.ReceitasBrutas.Should().Be(30_500_000_000.55m);
        indicadorLido.PopulacaoResidente2022.Should().Be(8121025);
        faixaLida.EstadoId.Should().Be(cenario.Para.Id);
        faixaLida.Descricao.Should().Be("Capital: Belém");
    }

    private static void ConferirCoordenada(Estado lido, double longitude, double latitude)
    {
        lido.Coordenada.Should().NotBeNull();
        lido.Coordenada!.X.Should().BeApproximately(longitude, 1e-6);
        lido.Coordenada.Y.Should().BeApproximately(latitude, 1e-6);
        lido.Coordenada.SRID.Should().Be(4326);
    }

    private async Task ExigirUfDuplicadaRejeitadaAsync()
    {
        // UF duplicada — verifica que falhou no ix_estado_uf, não em outra constraint.
        Pais pais = NovoPais(GeoTestKeys.SiglaIso(), "CL", "Chile");
        string uf = GeoTestKeys.Uf();
        Estado primeiro = NovoEstado(pais.Id, uf, "Estado A");
        Estado duplicado = NovoEstado(pais.Id, uf, "Estado A duplicata");

        await _banco.GravarAsync(pais, primeiro);
        await _banco.ExigirViolacaoUniqueAsync(duplicado, "ix_estado_uf");
    }

    private async Task ExigirSiglaIsoDuplicadaRejeitadaAsync()
    {
        string siglaIso = GeoTestKeys.SiglaIso();
        Pais primeiro = NovoPais(siglaIso, "BO", "Bolívia");
        Pais duplicado = NovoPais(siglaIso, "BO", "Bolívia duplicata");

        await _banco.GravarAsync(primeiro);
        await _banco.ExigirViolacaoUniqueAsync(duplicado, "ix_pais_sigla_iso");
    }

    private static Pais NovoPais(string siglaIso, string sigla, string nome, bool vigente = true) =>
        GeoTestKeys.Ok(Pais.Importar(siglaIso, sigla, nome, default, new ProvenienciaDaCarga(VersaoDataset, vigente)));

    private static Estado NovoEstado(Guid paisId, string uf, string nome) =>
        GeoTestKeys.Ok(Estado.Importar(
            new UfNoPais(paisId, uf), new NomeComFormaDeBusca(nome, null),
            default, null, default, default, new ProvenienciaDaCarga(VersaoDataset)));

    private static Estado NovoEstadoNaPosicao(Guid paisId, decimal latitude, decimal longitude) =>
        GeoTestKeys.Ok(Estado.Importar(
            new UfNoPais(paisId, GeoTestKeys.Uf()), new NomeComFormaDeBusca("São Paulo", null),
            default, null,
            new PosicaoGeografica(latitude, longitude, new Point((double)longitude, (double)latitude) { SRID = 4326 }),
            default, new ProvenienciaDaCarga(VersaoDataset)));

    private static EstadoIndicador NovoIndicadorSemDados(Guid estadoId) =>
        GeoTestKeys.Ok(EstadoIndicador.Importar(
            estadoId, gentilico: null, governador: null,
            metricas: new MetricasDoEstado(),
            proveniencia: new ProvenienciaDaCarga(VersaoDataset)));
}
