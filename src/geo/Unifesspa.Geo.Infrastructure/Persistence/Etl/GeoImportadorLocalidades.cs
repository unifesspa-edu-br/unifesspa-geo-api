namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Orquestra a carga das folhas do Geo (ADR-0092) dentro da transação única:
/// (1) <see cref="GeoImportadorDistritoBairro"/> faz o upsert de
/// Distrito/Bairro (+faixas), grande usuário e CEP geral e devolve os mapas de FK;
/// (2) <see cref="ImportadorDeLogradourosPorCopia"/> carrega complementos e logradouros em lote.
/// O topo (País/Estado/Cidade) deve estar carregado antes — a resolução de FK
/// depende das cidades.
/// </summary>
internal sealed partial class GeoImportadorLocalidades : IGeoImportadorLocalidades
{
    private readonly GeoImportadorDistritoBairro _distritoBairro;
    private readonly ImportadorDeLogradourosPorCopia _logradouro;
    private readonly ILogger<GeoImportadorLocalidades> _logger;

    public GeoImportadorLocalidades(
        GeoImportadorDistritoBairro distritoBairro,
        ImportadorDeLogradourosPorCopia logradouro,
        ILogger<GeoImportadorLocalidades> logger)
    {
        ArgumentNullException.ThrowIfNull(distritoBairro);
        ArgumentNullException.ThrowIfNull(logradouro);
        ArgumentNullException.ThrowIfNull(logger);

        _distritoBairro = distritoBairro;
        _logradouro = logradouro;
        _logger = logger;
    }

    public async Task<RelatorioImportacao> ImportarAsync(IGeoFonteDados fonte, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fonte);

        RelatorioImportacao relatorio = new(fonte.Versao);
        LogCargaIniciada(_logger, fonte.Versao);

        ResolucaoLocalidades resolucao =
            await _distritoBairro.ImportarAsync(fonte, relatorio, cancellationToken).ConfigureAwait(false);

        await _logradouro.ImportarAsync(fonte, resolucao, relatorio, cancellationToken).ConfigureAwait(false);

        LogCargaConcluida(_logger, fonte.Versao, relatorio.TotalInseridos, relatorio.TotalAtualizados, relatorio.TotalOrfaos, relatorio.TotalParsesDegradados);

        return relatorio;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo: carga das folhas iniciada (versão {Versao}).")]
    private static partial void LogCargaIniciada(ILogger logger, string versao);

    [LoggerMessage(Level = LogLevel.Information, Message = "Geo: carga das folhas concluída (versão {Versao}, inseridos={Inseridos}, atualizados={Atualizados}, órfãos={Orfaos}, degradados={Degradados}).")]
    private static partial void LogCargaConcluida(ILogger logger, string versao, int inseridos, int atualizados, int orfaos, int degradados);
}
