namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.Extensions.Configuration;

using Npgsql;

/// <summary>
/// Coordena a trava consultiva do PostgreSQL (<c>pg_try_advisory_lock</c>) que garante
/// exclusividade entre cargas da carga DNE e a reconciliação na partida, em conexão
/// dedicada liberada no descarte.
/// </summary>
internal sealed class TravaConsultivaDaCargaDne
{
    /// <summary>Identificador fixo da trava consultiva da carga DNE no PostgreSQL.</summary>
    public const long ChaveDaCargaDne = 6_409_200_101L;

    private readonly IConfiguration _configuration;

    public TravaConsultivaDaCargaDne(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    public async Task<IAsyncDisposable?> TentarAdquirirAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection conexao = new(ObterTextoDeConexao());
        try
        {
            return await AbrirETravarAsync(conexao, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await conexao.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<IAsyncDisposable?> AbrirETravarAsync(NpgsqlConnection conexao, CancellationToken cancellationToken)
    {
        await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!await TentarTravarNaConexaoAsync(conexao, cancellationToken).ConfigureAwait(false))
        {
            await conexao.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        return new SessaoDaTrava(conexao);
    }

    private string ObterTextoDeConexao() =>
        _configuration.GetConnectionString(GeoInfrastructureRegistration.ConnectionStringName)
        ?? throw new InvalidOperationException(
            $"ConnectionStrings:{GeoInfrastructureRegistration.ConnectionStringName} não configurada para a trava da carga Geo.");

    private static async Task<bool> TentarTravarNaConexaoAsync(NpgsqlConnection conexao, CancellationToken cancellationToken)
    {
        NpgsqlCommand comando = conexao.CreateCommand();
        await using (comando.ConfigureAwait(false))
        {
            comando.CommandText = "SELECT pg_try_advisory_lock(@chave)";
            comando.Parameters.AddWithValue("chave", ChaveDaCargaDne);
            object? resultado = await comando.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return resultado is true;
        }
    }

    private sealed class SessaoDaTrava : IAsyncDisposable
    {
        private readonly NpgsqlConnection _conexao;

        public SessaoDaTrava(NpgsqlConnection conexao)
        {
            _conexao = conexao;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await DestravarAsync().ConfigureAwait(false);
            }
            catch (NpgsqlException)
            {
                // Fechar a conexão na sequência já libera a trava de sessão no PostgreSQL.
            }
            catch (InvalidOperationException)
            {
                // Conexão já encerrada; o PostgreSQL libera a trava de sessão automaticamente.
            }
            finally
            {
                await _conexao.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task DestravarAsync()
        {
            if (_conexao.State != System.Data.ConnectionState.Open)
            {
                return;
            }

            NpgsqlCommand comando = _conexao.CreateCommand();
            await using (comando.ConfigureAwait(false))
            {
                comando.CommandText = "SELECT pg_advisory_unlock(@chave)";
                comando.Parameters.AddWithValue("chave", ChaveDaCargaDne);
                await comando.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
