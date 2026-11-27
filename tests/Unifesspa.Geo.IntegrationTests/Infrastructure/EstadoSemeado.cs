namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using Unifesspa.Geo.Domain.Entities;

/// <summary>País e estado com chaves naturais únicas, base dos testes de persistência.</summary>
internal sealed record EstadoSemeado(Pais Pais, Estado Estado)
{
    private const string VersaoDataset = "202601";

    public static EstadoSemeado Novo()
    {
        Pais pais = GeoTestKeys.Ok(Pais.Importar(GeoTestKeys.SiglaIso(), "BR", "Brasil", null, null, null, null, VersaoDataset));
        Estado estado = GeoTestKeys.Ok(Estado.Importar(pais.Id, GeoTestKeys.Uf(), "Estado seed", null, null, null, null, null, null, null, null, null, VersaoDataset));
        return new EstadoSemeado(pais, estado);
    }

    public object[] EntidadesCom(params object[] outras) => [Pais, Estado, .. outras];
}
