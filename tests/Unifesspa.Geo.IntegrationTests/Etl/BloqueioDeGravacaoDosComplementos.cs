namespace Unifesspa.Geo.IntegrationTests.Etl;

using Npgsql;

/// <summary>
/// Mantém a tabela <c>logradouro_complemento</c> bloqueada para gravação por outra sessão: a
/// leitura segue liberada (a contagem passa) e a mesclagem da carga espera. Ao descartar,
/// desfaz a transação e fecha a conexão, mesmo quando a verificação do teste falha.
/// </summary>
internal sealed class BloqueioDeGravacaoDosComplementos : IAsyncDisposable
{
    private readonly NpgsqlConnection _conexao;
    private readonly NpgsqlTransaction _transacao;

    private BloqueioDeGravacaoDosComplementos(NpgsqlConnection conexao, NpgsqlTransaction transacao)
    {
        _conexao = conexao;
        _transacao = transacao;
    }

    public static async Task<BloqueioDeGravacaoDosComplementos> AdquirirAsync(string textoDeConexao)
    {
        NpgsqlConnection conexao = new(textoDeConexao);
        try
        {
            await conexao.OpenAsync();
            return new BloqueioDeGravacaoDosComplementos(conexao, await BloquearAsync(conexao));
        }
        catch
        {
            await conexao.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // A transação nunca é confirmada: descartá-la desfaz o bloqueio.
        await _transacao.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private static async Task<NpgsqlTransaction> BloquearAsync(NpgsqlConnection conexao)
    {
        NpgsqlTransaction transacao = await conexao.BeginTransactionAsync();
        NpgsqlCommand comando = new("LOCK TABLE logradouro_complemento IN SHARE MODE", conexao, transacao);
        await using (comando)
        {
            await comando.ExecuteNonQueryAsync();
        }

        return transacao;
    }
}
