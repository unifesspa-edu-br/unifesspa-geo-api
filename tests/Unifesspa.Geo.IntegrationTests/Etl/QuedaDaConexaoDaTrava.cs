namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Npgsql;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>
/// Derruba, no servidor, a conexão que segura a trava consultiva da carga, como faz um limite de
/// inatividade ou uma rede que corta a conexão parada: a carga segue viva, mas sem a trava.
/// </summary>
internal static class QuedaDaConexaoDaTrava
{
    private const string Comando = """
        SELECT count(*) FROM (
            SELECT pg_terminate_backend(pid, 5000) AS derrubada
            FROM pg_locks
            WHERE locktype = 'advisory' AND objsubid = 1 AND granted
              AND ((classid::bigint << 32) | objid::bigint) = @chave
        ) quedas
        WHERE derrubada
        """;

    public static async Task DerrubarAsync(string textoDeConexao)
    {
        await using NpgsqlConnection conexao = new(textoDeConexao);
        await conexao.OpenAsync();
        await using NpgsqlCommand comando = new(Comando, conexao);
        comando.Parameters.AddWithValue("chave", TravaConsultivaDaCargaDne.ChaveDaCargaDne);

        long derrubadas = (long)(await comando.ExecuteScalarAsync())!;

        derrubadas.Should().Be(1, "exatamente uma conexão segurava a trava da carga");
    }
}
