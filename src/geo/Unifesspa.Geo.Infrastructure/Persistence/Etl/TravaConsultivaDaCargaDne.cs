namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

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

    /// <summary>Nome da sessão da trava no <c>pg_stat_activity</c>.</summary>
    internal const string NomeDaAplicacaoDaTrava = "unifesspa-geo-trava-da-carga";

    private readonly IConfiguration _configuration;
    private readonly IOptions<EtlOpcoes> _opcoes;

    public TravaConsultivaDaCargaDne(IConfiguration configuration, IOptions<EtlOpcoes> opcoes)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(opcoes);
        _configuration = configuration;
        _opcoes = opcoes;
    }

    public async Task<IAsyncDisposable?> TentarAdquirirAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection conexao = new(MontarTextoDeConexao());
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

    /// <summary>
    /// Texto de conexão da trava: o mesmo do banco do Geo, acrescido do sinal de vida do Npgsql
    /// (mantém a conexão parada ativa para o banco e para a rede) e de um nome de aplicação fixo.
    /// O nome faz o texto diferir sempre do usado pelo restante do Geo, o que dá à trava um conjunto
    /// de conexões próprio, e identifica a sessão no <c>pg_stat_activity</c>.
    /// </summary>
    internal string MontarTextoDeConexao()
    {
        double segundos = _opcoes.Value.IntervaloDoSinalDeVidaDaTrava.TotalSeconds;
        NpgsqlConnectionStringBuilder construtor = new(ObterTextoDeConexao())
        {
            KeepAlive = (int)Math.Ceiling(segundos),
            ApplicationName = NomeDaAplicacaoDaTrava,
        };

        return construtor.ConnectionString;
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
            catch (Exception excecao) when (excecao is NpgsqlException or InvalidOperationException)
            {
                // Falha ao destravar ou conexão já encerrada: o PostgreSQL libera a trava de sessão
                // quando a conexão é fechada.
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
