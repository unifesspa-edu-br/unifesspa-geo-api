namespace Unifesspa.Geo.IntegrationTests;

using AwesomeAssertions;

using Infrastructure;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence;

/// <summary>
/// Persistência de Distrito/Bairro (+faixas de CEP) contra Postgres+PostGIS real
/// (story #670). Sem código IBGE: chave natural <c>(cidade_id, nome_normalizado)</c>.
/// Fixture compartilhada — cada teste semeia sua própria cidade com chaves únicas.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class DistritoBairroPersistenceTests
{
    private const string VersaoDataset = "202601";

    private readonly GeoPostgisFixture _fixture;
    private readonly GravacaoNoBanco _banco;

    public DistritoBairroPersistenceTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new GravacaoNoBanco(fixture);
    }

    [Fact(DisplayName = "CA-01: Distrito e Bairro (+faixas) persistem vinculados à Cidade, com Point 4326")]
    public async Task DistritoBairro_PersisteELe()
    {
        CidadeSemeada seed = CidadeSemeada.Nova();
        CenarioDoDistritoEDoBairro cenario = CenarioDoDistritoEDoBairro.Montar(seed.Cidade.Id);

        await _banco.GravarAsync(seed.EntidadesCom(cenario.Entidades()));

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        await ConferirDistritoAsync(leitura, cenario, seed.Cidade.Id);
        await ConferirBairroAsync(leitura, cenario, seed.Cidade.Id);
    }

    [Fact(DisplayName = "CA-02: nome duplicado na mesma cidade é rejeitado (constraint composta cidade+nome)")]
    public async Task Distrito_NomeDuplicadoMesmaCidade_Rejeita()
    {
        CidadeSemeada seed = CidadeSemeada.Nova();
        Cidade cidade = seed.Cidade;

        Distrito d1 = NovoDistrito(cidade.Id, "Centro", "centro", "1");
        Distrito d2 = NovoDistrito(cidade.Id, "Centro", "centro", "2");

        await _banco.GravarAsync(seed.EntidadesCom(d1));
        await _banco.ExigirViolacaoUniqueAsync(d2, "ix_distrito_cidade_nome");
    }

    [Fact(DisplayName = "CA-02: chave natural é caixa-insensível por canonicalização (Centro==centro)")]
    public async Task Distrito_NomeNormalizado_CaixaInsensivel_Rejeita()
    {
        CidadeSemeada seed = CidadeSemeada.Nova();
        Cidade cidade = seed.Cidade;

        // nome_normalizado em caixas diferentes — a factory canonicaliza para minúsculas,
        // então ambos colidem na chave natural.
        Distrito maiusculo = NovoDistrito(cidade.Id, "Centro", "CENTRO", "1");
        Distrito minusculo = NovoDistrito(cidade.Id, "Centro", "centro", "2");

        maiusculo.NomeNormalizado.Should().Be(minusculo.NomeNormalizado, "a factory canonicaliza nome_normalizado em minúsculas");

        await _banco.GravarAsync(seed.EntidadesCom(maiusculo));
        await _banco.ExigirViolacaoUniqueAsync(minusculo, "ix_distrito_cidade_nome");
    }

    [Fact(DisplayName = "CA-03: id_origem_dne não é identidade — só (cidade+nome) define a unicidade")]
    public async Task IdOrigemDne_NaoIdentidade()
    {
        // d1 e d2 têm o MESMO (cidade, nome_normalizado) mas id_origem_dne distinto:
        // a unicidade não considera id_origem_dne, então a segunda carga é barrada.
        CidadeSemeada seed = CidadeSemeada.Nova();
        Cidade cidade = seed.Cidade;
        Distrito d1 = NovoDistrito(cidade.Id, "Vila A", "vila a", "9001");
        Distrito d2 = NovoDistrito(cidade.Id, "Vila A", "vila a", "9999");

        await _banco.GravarAsync(seed.EntidadesCom(d1));
        await _banco.ExigirViolacaoUniqueAsync(d2, "ix_distrito_cidade_nome");
    }

    [Fact(DisplayName = "CA-02: mesmo nome em cidades distintas coexiste (bairro)")]
    public async Task Bairro_MesmoNomeCidadesDistintas_Aceita()
    {
        CidadeSemeada seedX = CidadeSemeada.Nova();
        CidadeSemeada seedY = CidadeSemeada.Nova();

        Bairro bX = GeoTestKeys.Ok(Bairro.Importar(
            seedX.Cidade.Id, "SP", new NomeComFormaDeBusca("Centro", "centro"), default, null, new ProvenienciaDaCarga(VersaoDataset)));
        Bairro bY = GeoTestKeys.Ok(Bairro.Importar(
            seedY.Cidade.Id, "RJ", new NomeComFormaDeBusca("Centro", "centro"), default, null, new ProvenienciaDaCarga(VersaoDataset)));

        await _banco.GravarAsync([.. seedX.EntidadesCom(), .. seedY.EntidadesCom(), bX, bY]);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        int coexistentes = await leitura.Bairros.CountAsync(b => b.Id == bX.Id || b.Id == bY.Id);
        coexistentes.Should().Be(2, "a chave é (cidade+nome); cidades distintas comportam o mesmo bairro");
    }

    private static async Task ConferirDistritoAsync(GeoDbContext leitura, CenarioDoDistritoEDoBairro cenario, Guid cidadeId)
    {
        Distrito distritoLido = await leitura.Distritos.SingleAsync(d => d.Id == cenario.Distrito.Id);
        DistritoFaixaCep dFaixa = await leitura.DistritoFaixasCep.SingleAsync(f => f.Id == cenario.DistritoFaixa.Id);

        distritoLido.Id.Version.Should().Be(7, "EntityBase usa Guid v7 (ADR-0032)");
        distritoLido.CidadeId.Should().Be(cidadeId);
        distritoLido.Coordenada.Should().NotBeNull();
        distritoLido.Coordenada!.SRID.Should().Be(4326);
        distritoLido.IdOrigemDne.Should().Be("1001");
        dFaixa.DistritoId.Should().Be(cenario.Distrito.Id);
    }

    private static async Task ConferirBairroAsync(GeoDbContext leitura, CenarioDoDistritoEDoBairro cenario, Guid cidadeId)
    {
        Bairro bairroLido = await leitura.Bairros.SingleAsync(b => b.Id == cenario.Bairro.Id);
        BairroFaixaCep bFaixa = await leitura.BairroFaixasCep.SingleAsync(f => f.Id == cenario.BairroFaixa.Id);

        bairroLido.CidadeId.Should().Be(cidadeId);
        bairroLido.Coordenada!.SRID.Should().Be(4326);
        bFaixa.BairroId.Should().Be(cenario.Bairro.Id);
    }

    private static Distrito NovoDistrito(Guid cidadeId, string nome, string nomeNormalizado, string idOrigemDne) =>
        GeoTestKeys.Ok(Distrito.Importar(
            cidadeId, "SP", new NomeComFormaDeBusca(nome, nomeNormalizado), default, idOrigemDne, new ProvenienciaDaCarga(VersaoDataset)));
}
