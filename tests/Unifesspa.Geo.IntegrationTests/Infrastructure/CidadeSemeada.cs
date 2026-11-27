namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using Unifesspa.Geo.Domain.Entities;

/// <summary>País, estado e cidade com chaves naturais únicas, base dos testes de persistência.</summary>
internal sealed record CidadeSemeada(EstadoSemeado Estado, Cidade Cidade)
{
    private const string VersaoDataset = "202601";

    public static CidadeSemeada Nova()
    {
        EstadoSemeado estado = EstadoSemeado.Novo();
        Cidade cidade = GeoTestKeys.Ok(Cidade.Importar(
            estado.Estado.Id, "SP", GeoTestKeys.CodigoIbge(), "Cidade seed", "cidade seed", null,
            null, null, null, null, null, null, null, null, null, null, null, VersaoDataset));
        return new CidadeSemeada(estado, cidade);
    }

    public object[] EntidadesCom(params object[] outras) =>
        Estado.EntidadesCom([Cidade, .. outras]);
}
