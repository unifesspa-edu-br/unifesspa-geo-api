namespace Unifesspa.Geo.IntegrationTests;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>Bairro, logradouro, complemento e grande usuário completos, para provar a gravação e a releitura.</summary>
internal sealed record CenarioDoLogradouroDaSe(
    Bairro Bairro,
    Logradouro Logradouro,
    LogradouroComplemento Complemento,
    CepGrandeUsuario GrandeUsuario)
{
    private const string VersaoDataset = "202601";

    public static CenarioDoLogradouroDaSe Montar(Guid cidadeId)
    {
        Bairro bairro = GeoTestKeys.Ok(Bairro.Importar(
            cidadeId, "SP", new NomeComFormaDeBusca("Sé", "se"), default, null, new ProvenienciaDaCarga(VersaoDataset)));
        string cep = GeoTestKeys.Cep();
        return new CenarioDoLogradouroDaSe(bairro, NovoLogradouro(cep, cidadeId, bairro.Id), NovoComplemento(cep), NovoGrandeUsuario());
    }

    public object[] Entidades() => [Bairro, Logradouro, Complemento, GrandeUsuario];

    private static Logradouro NovoLogradouro(string cep, Guid cidadeId, Guid bairroId) =>
        GeoTestKeys.Ok(Logradouro.Importar(
            cep: cep, tipo: "Praça", nome: "da Sé", nomeCompleto: "Praça da Sé",
            nomeNormalizado: "praca da se", cidadeId: cidadeId, distritoId: null, bairroId: bairroId,
            uf: "SP", latitude: -23.55052m, longitude: -46.63331m,
            coordenada: new Point(-46.63331, -23.55052) { SRID = 4326 },
            ativo: true, versaoDataset: VersaoDataset));

    private static LogradouroComplemento NovoComplemento(string cep) =>
        GeoTestKeys.Ok(LogradouroComplemento.Importar(cep, "lado par", "lado par", VersaoDataset));

    private static CepGrandeUsuario NovoGrandeUsuario() =>
        GeoTestKeys.Ok(CepGrandeUsuario.Importar(GeoTestKeys.Cep(), "UNESP", "unesp", VersaoDataset));
}
