namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using Npgsql;

/// <summary>Executa um <see cref="ComandoDoEsquemaDeCarga"/>, sem parâmetros.</summary>
internal static class ExecutorDeComandosDoEsquema
{
    public static async Task ExecutarAsync(NpgsqlConnection conexao, ComandoDoEsquemaDeCarga comando, CancellationToken cancellationToken)
    {
        NpgsqlBatch lote = conexao.CreateBatch();
        await using (lote.ConfigureAwait(false))
        {
            lote.BatchCommands.Add(new NpgsqlBatchCommand(comando.Texto));
            await lote.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
