namespace Unifesspa.Geo.IntegrationTests;

using AwesomeAssertions;

using Infrastructure;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;

/// <summary>
/// Persistência de Cidade (territorial embutido) + CidadeIndicador (1:1) +
/// CidadeFaixaCep contra Postgres+PostGIS real (story #669). Fixture compartilhada:
/// chaves naturais únicas por teste (<see cref="GeoTestKeys"/>), exceto o teste de
/// duplicata.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CidadePersistenceTests
{
    private const string VersaoDataset = "202601";

    private readonly GeoPostgisFixture _fixture;
    private readonly GravacaoNoBanco _banco;

    public CidadePersistenceTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new GravacaoNoBanco(fixture);
    }

    [Fact(DisplayName = "CA-01: Cidade (territorial) + Indicador(1:1) + Faixa persistem e relêem com Point 4326")]
    public async Task Cidade_PersisteELe()
    {
        EstadoSemeado semeado = EstadoSemeado.Novo();
        CenarioDaCidadeDeMaraba cenario = CenarioDaCidadeDeMaraba.Montar(semeado.Estado.Id);

        await _banco.GravarAsync(semeado.EntidadesCom(cenario.Entidades()));

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        await ConferirCidadeAsync(leitura, cenario, semeado.Estado.Id);
        await ConferirIndicadorEFaixaAsync(leitura, cenario);
    }

    [Fact(DisplayName = "CA-02: código IBGE duplicado é rejeitado pela constraint única correta")]
    public async Task Cidade_CodigoIbgeDuplicado_Rejeita()
    {
        EstadoSemeado semeado = EstadoSemeado.Novo();
        string codigoIbge = GeoTestKeys.CodigoIbge();

        Cidade c1 = NovaCidade(semeado.Estado.Id, "PA", codigoIbge, "Cidade A");
        Cidade c2 = NovaCidade(semeado.Estado.Id, "PA", codigoIbge, "Cidade A duplicata");

        await _banco.GravarAsync(semeado.EntidadesCom(c1));
        await _banco.ExigirViolacaoUniqueAsync(c2, "ix_cidade_codigo_ibge");
    }

    [Fact(DisplayName = "CA-02: homônimos em UFs distintas coexistem (chave é o código IBGE, não o nome)")]
    public async Task Cidade_HomonimosUfDistintas_Aceita()
    {
        EstadoSemeado semeado = EstadoSemeado.Novo();
        Estado rs = GeoTestKeys.Ok(Estado.Importar(semeado.Pais.Id, GeoTestKeys.Uf(), "Rio Grande do Sul", null, null, null, null, null, null, null, null, null, VersaoDataset));

        Cidade bomJesusPi = NovaCidade(semeado.Estado.Id, "PI", GeoTestKeys.CodigoIbge(), "Bom Jesus");
        Cidade bomJesusRs = NovaCidade(rs.Id, "RS", GeoTestKeys.CodigoIbge(), "Bom Jesus");

        await _banco.GravarAsync(semeado.EntidadesCom(rs, bomJesusPi, bomJesusRs));

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        int homonimos = await leitura.Cidades.CountAsync(c => c.Nome == "Bom Jesus"
            && (c.Id == bomJesusPi.Id || c.Id == bomJesusRs.Id));
        homonimos.Should().Be(2);
    }

    [Fact(DisplayName = "CA-03: CidadeIndicador com mortalidade ausente persiste null sem erro")]
    public async Task CidadeIndicador_DadoAusente_Null()
    {
        EstadoSemeado semeado = EstadoSemeado.Novo();
        Cidade cidade = NovaCidade(semeado.Estado.Id, "PA", GeoTestKeys.CodigoIbge(), "Parauapebas");
        CidadeIndicador indicador = NovoIndicadorSemDados(cidade.Id);

        await _banco.GravarAsync(semeado.EntidadesCom(cidade, indicador));

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        CidadeIndicador lido = await leitura.CidadeIndicadores.SingleAsync(i => i.CidadeId == cidade.Id);
        lido.MortalidadeInfantil.Should().BeNull();
        lido.Idh.Should().BeNull();
    }

    private static async Task ConferirCidadeAsync(GeoDbContext leitura, CenarioDaCidadeDeMaraba cenario, Guid estadoId)
    {
        Cidade lida = await leitura.Cidades.SingleAsync(c => c.Id == cenario.Maraba.Id);

        lida.Id.Version.Should().Be(7, "EntityBase usa Guid v7 (ADR-0032)");
        lida.EstadoId.Should().Be(estadoId);
        lida.MesorregiaoNome.Should().Be("Sudeste Paraense");
        lida.RegiaoImediataNome.Should().Be("Marabá");
        lida.Coordenada.Should().NotBeNull();
        lida.Coordenada!.SRID.Should().Be(4326);
    }

    private static async Task ConferirIndicadorEFaixaAsync(GeoDbContext leitura, CenarioDaCidadeDeMaraba cenario)
    {
        CidadeIndicador indicadorLido = await leitura.CidadeIndicadores.SingleAsync(i => i.CidadeId == cenario.Maraba.Id);
        CidadeFaixaCep faixaLida = await leitura.CidadeFaixasCep.SingleAsync(f => f.Id == cenario.Faixa.Id);

        indicadorLido.Aniversario.Should().Be("05/04");
        indicadorLido.MortalidadeInfantil.Should().Be(17.2m);
        faixaLida.CidadeId.Should().Be(cenario.Maraba.Id);
    }

    private static Cidade NovaCidade(Guid estadoId, string uf, string codigoIbge, string nome) =>
        GeoTestKeys.Ok(Cidade.Importar(
            estadoId, uf, codigoIbge, nome, nome, null,
            null, null, null,
            null, null, null, null, null, null, null, null,
            VersaoDataset));

    private static CidadeIndicador NovoIndicadorSemDados(Guid cidadeId) =>
        GeoTestKeys.Ok(CidadeIndicador.Importar(
            cidadeId: cidadeId, gentilico: null, prefeito: null,
            areaKm2: null, populacaoResidente: null, densidadeDemografica: null,
            escolarizacao6a14: null, idh: null, mortalidadeInfantil: null,
            receitas: null, despesas: null, pibPerCapita: null,
            aniversario: null, versaoDataset: VersaoDataset));
}
