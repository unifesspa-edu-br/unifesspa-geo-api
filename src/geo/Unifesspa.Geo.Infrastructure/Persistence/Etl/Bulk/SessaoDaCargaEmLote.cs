namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using System.Diagnostics.CodeAnalysis;

using Npgsql;

/// <summary>
/// Conexão única e dedicada da carga em lote. A cópia, a mesclagem e a criação dos índices
/// precisam compartilhar a mesma sessão do banco, e a conexão nunca abre transação:
/// <c>CREATE INDEX CONCURRENTLY</c> não roda dentro de uma. Todo comando criado aqui recebe o
/// prazo configurado, menos a cópia binária, que não tem prazo.
/// </summary>
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Todo o SQL que chega a esta classe é literal de código (criação e remoção de tabelas temporárias, índices, cópia e mesclagem constantes); os nomes de tabela vêm de uma lista fixa de literais. O único valor parametrizado (@now) é um NpgsqlParameter. Não há entrada de usuário — COPY e DDL não são parametrizáveis por identificador.")]
internal sealed class SessaoDaCargaEmLote : IAsyncDisposable
{
    private readonly NpgsqlDataSource _fonteDeDados;
    private readonly NpgsqlConnection _conexao;
    private readonly int _prazoDosComandosEmSegundos;

    private SessaoDaCargaEmLote(NpgsqlDataSource fonteDeDados, NpgsqlConnection conexao, int prazoDosComandosEmSegundos)
    {
        _fonteDeDados = fonteDeDados;
        _conexao = conexao;
        _prazoDosComandosEmSegundos = prazoDosComandosEmSegundos;
    }

    public static async Task<SessaoDaCargaEmLote> AbrirAsync(ConfiguracaoDaCargaEmLote configuracao, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        NpgsqlDataSource fonteDeDados = CriarFonteDeDados(configuracao.TextoDeConexao);
        try
        {
            NpgsqlConnection conexao = await fonteDeDados.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            return new SessaoDaCargaEmLote(fonteDeDados, conexao, configuracao.PrazoDosComandosEmSegundos);
        }
        catch
        {
            await fonteDeDados.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task ExecutarAsync(string comando, CancellationToken cancellationToken)
    {
        NpgsqlCommand npgsqlComando = CriarComando(comando);
        await using (npgsqlComando.ConfigureAwait(false))
        {
            await npgsqlComando.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<long> ContarLinhasAsync(string tabela, CancellationToken cancellationToken)
    {
        // O nome da tabela é literal de código (lista fixa), não entrada de usuário.
        NpgsqlCommand comando = CriarComando($"SELECT count(*) FROM {tabela}");
        await using (comando.ConfigureAwait(false))
        {
            object? resultado = await comando.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return resultado is long linhas ? linhas : 0;
        }
    }

    public async Task<long> MesclarAsync(string comandoDeMesclagem, DateTimeOffset instante, CancellationToken cancellationToken)
    {
        NpgsqlCommand comando = CriarComando(comandoDeMesclagem);
        await using (comando.ConfigureAwait(false))
        {
            comando.Parameters.AddWithValue("now", instante);
            return await comando.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<NpgsqlBinaryImporter> IniciarCopiaAsync(string comandoDeCopia, CancellationToken cancellationToken) =>
        _conexao.BeginBinaryImportAsync(comandoDeCopia, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _conexao.DisposeAsync().ConfigureAwait(false);
        await _fonteDeDados.DisposeAsync().ConfigureAwait(false);
    }

    // Fonte de dados própria com NetTopologySuite em modo geography: a cópia binária escreve o
    // Point como geography(Point,4326), igual à coluna. No modo padrão (geometry) o EWKB seria
    // incompatível com ela.
    private static NpgsqlDataSource CriarFonteDeDados(string textoDeConexao)
    {
        NpgsqlDataSourceBuilder construtor = new(textoDeConexao);
        construtor.UseNetTopologySuite(geographyAsDefault: true);
        return construtor.Build();
    }

    private NpgsqlCommand CriarComando(string texto)
    {
        NpgsqlCommand comando = _conexao.CreateCommand();
        comando.CommandTimeout = _prazoDosComandosEmSegundos;
        comando.CommandText = texto;
        return comando;
    }
}
