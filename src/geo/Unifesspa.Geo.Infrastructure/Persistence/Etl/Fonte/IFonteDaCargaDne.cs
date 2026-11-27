namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Fonte do dataset DNE de uma execução. Ao ser descartada, apaga o que a execução criou para
/// ela (o esquema de carga e o arquivo do pacote).
/// </summary>
internal interface IFonteDaCargaDne : IGeoFonteDados, IAsyncDisposable
{
}
