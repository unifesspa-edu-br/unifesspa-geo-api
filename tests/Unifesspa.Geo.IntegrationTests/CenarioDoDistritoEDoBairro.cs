namespace Unifesspa.Geo.IntegrationTests;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>Distrito, bairro e as faixas de CEP de cada um, para provar a gravação e a releitura.</summary>
internal sealed record CenarioDoDistritoEDoBairro(
    Distrito Distrito,
    Bairro Bairro,
    DistritoFaixaCep DistritoFaixa,
    BairroFaixaCep BairroFaixa)
{
    private const string VersaoDataset = "202601";

    public static CenarioDoDistritoEDoBairro Montar(Guid cidadeId)
    {
        Distrito distrito = NovoDistrito(cidadeId);
        Bairro bairro = NovoBairro(cidadeId);
        return new CenarioDoDistritoEDoBairro(
            distrito,
            bairro,
            GeoTestKeys.Ok(DistritoFaixaCep.Importar(distrito.Id, "02000000", "02099999", VersaoDataset)),
            GeoTestKeys.Ok(BairroFaixaCep.Importar(bairro.Id, "01000000", "01099999", VersaoDataset)));
    }

    public object[] Entidades() => [Distrito, Bairro, DistritoFaixa, BairroFaixa];

    private static Distrito NovoDistrito(Guid cidadeId) =>
        GeoTestKeys.Ok(Distrito.Importar(
            cidadeId: cidadeId, uf: "SP", nome: "Zona Norte", nomeNormalizado: "zona norte",
            latitude: -23.45m, longitude: -46.62m,
            coordenada: new Point(-46.62, -23.45) { SRID = 4326 },
            idOrigemDne: "1001", versaoDataset: VersaoDataset));

    private static Bairro NovoBairro(Guid cidadeId) =>
        GeoTestKeys.Ok(Bairro.Importar(
            cidadeId: cidadeId, uf: "SP", nome: "Sé", nomeNormalizado: "se",
            latitude: -23.55m, longitude: -46.63m,
            coordenada: new Point(-46.63, -23.55) { SRID = 4326 },
            idOrigemDne: "2002", versaoDataset: VersaoDataset));
}
