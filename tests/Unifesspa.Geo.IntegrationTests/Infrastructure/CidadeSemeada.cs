namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;

/// <summary>País, estado e cidade com chaves naturais únicas, base dos testes de persistência.</summary>
internal sealed record CidadeSemeada(EstadoSemeado Estado, Cidade Cidade)
{
    private const string VersaoDataset = "202601";

    public static CidadeSemeada Nova()
    {
        EstadoSemeado estado = EstadoSemeado.Novo();
        Cidade cidade = GeoTestKeys.Ok(Cidade.Importar(
            GeoTestKeys.CodigoIbge(), new EstadoDaCidade(estado.Estado.Id, "SP"),
            new NomeComFormaDeBusca("Cidade seed", "cidade seed"), null,
            default, default, new ProvenienciaDaCarga(VersaoDataset)));
        return new CidadeSemeada(estado, cidade);
    }

    public object[] EntidadesCom(params object[] outras) =>
        Estado.EntidadesCom([Cidade, .. outras]);
}
