namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>Obtém a trava consultiva da carga DNE, em conexão própria, como o pedido real faz.</summary>
internal static class TravaDaCargaParaTeste
{
    public static TravaConsultivaDaCargaDne Criar(string textoDeConexao) => Criar(textoDeConexao, new EtlOpcoes());

    public static TravaConsultivaDaCargaDne Criar(string textoDeConexao, EtlOpcoes opcoes)
    {
        IConfiguration configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ConnectionStrings:GeoDb"] = textoDeConexao })
            .Build();

        return new TravaConsultivaDaCargaDne(configuracao, Options.Create(opcoes));
    }

    public static async Task<IAsyncDisposable> AdquirirAsync(string textoDeConexao)
    {
        IAsyncDisposable? trava = await Criar(textoDeConexao).TentarAdquirirAsync(CancellationToken.None);
        trava.Should().NotBeNull("nenhuma outra sessão deve segurar a trava da carga neste momento");
        return trava!;
    }

    public static async Task<CargaReservada> ReservarAsync(string textoDeConexao, Guid execucaoId) =>
        CargaReservada.Reservar(execucaoId, await AdquirirAsync(textoDeConexao));
}
