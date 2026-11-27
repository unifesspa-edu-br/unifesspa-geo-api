namespace Unifesspa.Geo.IntegrationTests;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>Cidade, indicador e faixa de CEP completos, para provar a gravação e a releitura.</summary>
internal sealed record CenarioDaCidadeDeMaraba(Cidade Maraba, CidadeIndicador Indicador, CidadeFaixaCep Faixa)
{
    private const string VersaoDataset = "202601";

    public static CenarioDaCidadeDeMaraba Montar(Guid estadoId)
    {
        Cidade maraba = NovaCidade(estadoId);
        return new CenarioDaCidadeDeMaraba(maraba, NovoIndicador(maraba), NovaFaixa(maraba));
    }

    public object[] Entidades() => [Maraba, Indicador, Faixa];

    private static Cidade NovaCidade(Guid estadoId) =>
        GeoTestKeys.Ok(Cidade.Importar(
            estadoId: estadoId, uf: "PA", codigoIbge: GeoTestKeys.CodigoIbge(), nome: "Marabá",
            nomeNormalizado: "maraba", ddd: "94",
            latitude: -5.36867m, longitude: -49.11731m,
            coordenada: new Point(-49.11731, -5.36867) { SRID = 4326 },
            mesorregiaoCodigo: "1503", mesorregiaoNome: "Sudeste Paraense",
            microrregiaoCodigo: "15009", microrregiaoNome: "Marabá",
            regiaoIntermediariaCodigo: "1503", regiaoIntermediariaNome: "Marabá",
            regiaoImediataCodigo: "150009", regiaoImediataNome: "Marabá",
            versaoDataset: VersaoDataset));

    private static CidadeIndicador NovoIndicador(Cidade maraba) =>
        GeoTestKeys.Ok(CidadeIndicador.Importar(
            cidadeId: maraba.Id, gentilico: "marabaense", prefeito: null,
            areaKm2: 15128.058m, populacaoResidente: 233669, densidadeDemografica: 15.45m,
            escolarizacao6a14: 96.8m, idh: 0.668m, mortalidadeInfantil: 17.2m,
            receitas: 1_200_000_000.00m, despesas: 1_100_000_000.00m, pibPerCapita: 28000.50m,
            aniversario: "05/04", versaoDataset: VersaoDataset));

    private static CidadeFaixaCep NovaFaixa(Cidade maraba) =>
        GeoTestKeys.Ok(CidadeFaixaCep.Importar(
            cidadeId: maraba.Id, cepInicial: "68500000", cepFinal: "68519999",
            versaoDataset: VersaoDataset));
}
