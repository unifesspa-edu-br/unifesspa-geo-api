namespace Unifesspa.Geo.IntegrationTests.Etl;

using Npgsql;

/// <summary>
/// Espera, consultando o servidor, até que uma gravação da execução da carga fique aguardando o
/// bloqueio de uma linha que outra transação segura, sem depender de tempo fixo.
/// </summary>
internal static class EsperaPeloBloqueioNoBanco
{
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Intervalo = TimeSpan.FromMilliseconds(50);

    private const string Comando = """
        SELECT count(*) FROM pg_stat_activity
        WHERE datname = current_database()
          AND wait_event_type = 'Lock'
          AND query ILIKE '%update%geo_importacao_execucao%'
        """;

    public static async Task GravacaoDaExecucaoAguardandoAsync(string textoDeConexao)
    {
        await using NpgsqlConnection conexao = new(textoDeConexao);
        await conexao.OpenAsync();
        await using NpgsqlCommand comando = new(Comando, conexao);
        DateTimeOffset limite = DateTimeOffset.UtcNow + Prazo;

        while ((long)(await comando.ExecuteScalarAsync())! == 0)
        {
            if (DateTimeOffset.UtcNow > limite)
            {
                throw new TimeoutException("Nenhuma gravação da execução ficou aguardando o bloqueio da linha.");
            }

            await Task.Delay(Intervalo);
        }
    }
}
