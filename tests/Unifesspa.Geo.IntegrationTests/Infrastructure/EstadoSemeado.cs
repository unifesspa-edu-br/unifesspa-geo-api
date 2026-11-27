namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;

/// <summary>País e estado com chaves naturais únicas, base dos testes de persistência.</summary>
internal sealed record EstadoSemeado(Pais Pais, Estado Estado)
{
    private const string VersaoDataset = "202601";

    public static EstadoSemeado Novo()
    {
        Pais pais = GeoTestKeys.Ok(Pais.Importar(GeoTestKeys.SiglaIso(), "BR", "Brasil", default, new ProvenienciaDaCarga(VersaoDataset)));
        Estado estado = GeoTestKeys.Ok(Estado.Importar(
            new UfNoPais(pais.Id, GeoTestKeys.Uf()), new NomeComFormaDeBusca("Estado seed", null),
            default, null, default, default, new ProvenienciaDaCarga(VersaoDataset)));
        return new EstadoSemeado(pais, estado);
    }

    public object[] EntidadesCom(params object[] outras) => [Pais, Estado, .. outras];
}
