namespace Unifesspa.Geo.IntegrationTests;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>País, estado, indicador e faixa de CEP completos, para provar a gravação e a releitura.</summary>
internal sealed record CenarioDoEstadoDoPara(Pais Brasil, Estado Para, EstadoIndicador Indicador, EstadoFaixaCep Faixa)
{
    private const string VersaoDataset = "202601";

    public static CenarioDoEstadoDoPara Montar()
    {
        Pais brasil = NovoPais();
        Estado para = NovoEstado(brasil);
        return new CenarioDoEstadoDoPara(brasil, para, NovoIndicador(para), NovaFaixa(para));
    }

    public object[] Entidades() => [Brasil, Para, Indicador, Faixa];

    private static Pais NovoPais() =>
        GeoTestKeys.Ok(Pais.Importar(
            siglaIso: GeoTestKeys.SiglaIso(), sigla: "BR", nome: "Brasil",
            codigos: new CodigosDoPaisNosCadastrosFederais("1058", "105", null, null),
            proveniencia: new ProvenienciaDaCarga(VersaoDataset)));

    private static Estado NovoEstado(Pais brasil) =>
        GeoTestKeys.Ok(Estado.Importar(
            uf: new UfNoPais(brasil.Id, GeoTestKeys.Uf()),
            nome: new NomeComFormaDeBusca("Pará", "para"),
            classificacao: new ClassificacaoDaUfNoIbge("15", "Norte"),
            capital: "Belém",
            posicao: new PosicaoGeografica(-1.45502m, -48.5024m, new Point(-48.5024, -1.45502) { SRID = 4326 }),
            faixaGeral: new FaixaDeCep("66000000", "68899999"),
            proveniencia: new ProvenienciaDaCarga(VersaoDataset)));

    private static EstadoIndicador NovoIndicador(Estado para) =>
        GeoTestKeys.Ok(EstadoIndicador.Importar(
            estadoId: para.Id, gentilico: "paraense", governador: null,
            areaKm2: 1245870.704m, populacaoResidente2022: 8121025, densidadeDemografica: 6.52m,
            matriculasEnsinoFundamental2023: null, idh: 0.690m,
            receitasBrutas: 30_500_000_000.55m, despesasBrutas: 28_000_000_000.42m,
            rendimentoMensalPerCapita: null, totalVeiculos2023: 1_900_000,
            versaoDataset: VersaoDataset));

    private static EstadoFaixaCep NovaFaixa(Estado para) =>
        GeoTestKeys.Ok(EstadoFaixaCep.Importar(
            estadoId: para.Id, cepInicial: "66000000", cepFinal: "66999999",
            descricao: "Capital: Belém", versaoDataset: VersaoDataset));
}
