namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Entrega a fonte do dataset DNE de uma execução e limpa o que a carga deixa para trás. Em
/// produção a fonte é o esquema de carga populado a partir do pacote enviado; em teste, uma
/// fonte em memória.
/// </summary>
internal interface IFonteDaExecucaoDne
{
    /// <summary>
    /// Prepara a fonte da <paramref name="versao"/> para a execução. Se a preparação falhar, o que ela
    /// criou já foi descartado.
    /// </summary>
    Task<IFonteDaCargaDne> PrepararAsync(Guid execucaoId, string versao, CancellationToken cancellationToken);

    /// <summary>Apaga o arquivo do pacote da execução, se ainda existir.</summary>
    Task DescartarPacoteAsync(Guid execucaoId);

    /// <summary>Descarta todo esquema de carga que sobrou de uma carga interrompida; devolve quantos removeu.</summary>
    Task<int> DescartarSobrasAsync(CancellationToken cancellationToken);
}
