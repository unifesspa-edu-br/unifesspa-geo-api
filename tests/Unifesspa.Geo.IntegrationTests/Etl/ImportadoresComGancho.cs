namespace Unifesspa.Geo.IntegrationTests.Etl;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Importa o topo pelo importador real e, logo depois, executa o gancho do teste dentro da
/// mesma transação (para observá-la ou para interromper a carga naquele ponto).
/// </summary>
internal sealed class TopoComGancho : IGeoImportador
{
    private readonly IGeoImportador _interno;
    private readonly Func<Task> _depois;

    public TopoComGancho(IGeoImportador interno, Func<Task> depois)
    {
        _interno = interno;
        _depois = depois;
    }

    public async Task<RelatorioImportacao> ImportarAsync(IGeoFonteDados fonte, CancellationToken cancellationToken)
    {
        RelatorioImportacao relatorio = await _interno.ImportarAsync(fonte, cancellationToken);
        await _depois();
        return relatorio;
    }
}

/// <summary>
/// Importa as folhas (distritos, bairros, complementos e logradouros) pelo importador real e,
/// logo depois, executa o gancho do teste dentro da mesma transação.
/// </summary>
internal sealed class FolhasComGancho : IGeoImportadorLocalidades
{
    private readonly IGeoImportadorLocalidades _interno;
    private readonly Func<Task> _depois;

    public FolhasComGancho(IGeoImportadorLocalidades interno, Func<Task> depois)
    {
        _interno = interno;
        _depois = depois;
    }

    public async Task<RelatorioImportacao> ImportarAsync(IGeoFonteDados fonte, CancellationToken cancellationToken)
    {
        RelatorioImportacao relatorio = await _interno.ImportarAsync(fonte, cancellationToken);
        await _depois();
        return relatorio;
    }
}
