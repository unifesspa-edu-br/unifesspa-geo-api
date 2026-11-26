namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.Extensions.Logging;

/// <summary>Mensagens de log da carga de país, estado e cidade (ADR-0092).</summary>
internal static partial class GeoEtlLog
{
    public static void TabelaConcluida(ILogger logger, ContadorTabela contador)
    {
        ArgumentNullException.ThrowIfNull(contador);
        LogTabelaConcluida(logger, contador.Tabela, contador.Lidos, contador.Inseridos, contador.Atualizados);
    }

    public static void CargaConcluida(ILogger logger, RelatorioImportacao relatorio)
    {
        ArgumentNullException.ThrowIfNull(relatorio);
        LogCargaConcluida(
            logger,
            relatorio.VersaoDataset,
            relatorio.TotalInseridos,
            relatorio.TotalAtualizados,
            relatorio.TotalOrfaos,
            relatorio.TotalParsesDegradados);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ETL Geo: carga iniciada (versão {Versao}).")]
    public static partial void LogCargaIniciada(ILogger logger, string versao);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ETL Geo: país-âncora 'BRA' ausente no dataset da versão {Versao} — nenhum estado/cidade será carregado.")]
    public static partial void LogBrasilAusente(ILogger logger, string versao);

    [LoggerMessage(Level = LogLevel.Information, Message = "ETL Geo: tabela {Tabela} concluída (lidos={Lidos}, inseridos={Inseridos}, atualizados={Atualizados}).")]
    private static partial void LogTabelaConcluida(ILogger logger, string tabela, int lidos, int inseridos, int atualizados);

    [LoggerMessage(Level = LogLevel.Information, Message = "ETL Geo: carga concluída (versão {Versao}, inseridos={Inseridos}, atualizados={Atualizados}, órfãos={Orfaos}, degradados={Degradados}).")]
    private static partial void LogCargaConcluida(ILogger logger, string versao, int inseridos, int atualizados, int orfaos, int degradados);
}
