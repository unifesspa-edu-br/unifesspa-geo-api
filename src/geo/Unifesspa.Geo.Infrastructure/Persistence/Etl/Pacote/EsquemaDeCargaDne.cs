namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Npgsql;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Esquema de carga <c>carga_dne_{execucaoId}</c> no banco do Geo, como fonte da execução: cria o
/// esquema com tabelas <c>UNLOGGED</c> a partir do pacote enviado, apaga o arquivo do pacote assim que o
/// esquema está carregado, e entrega a fonte que descarta o esquema ao ser descartada. Descarta também
/// os esquemas que sobraram de uma carga interrompida: só os de nome exato de uma carga.
/// </summary>
internal sealed partial class EsquemaDeCargaDne : IFonteDaExecucaoDne
{
    private const string ConsultaDosEsquemasDeCarga =
        "SELECT nspname FROM pg_namespace WHERE starts_with(nspname, 'carga_dne_')";

    private readonly IConfiguration _configuration;
    private readonly ArquivoDoPacoteDne _arquivos;
    private readonly ILogger<EsquemaDeCargaDne> _logger;

    public EsquemaDeCargaDne(IConfiguration configuration, ArquivoDoPacoteDne arquivos, ILogger<EsquemaDeCargaDne> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(arquivos);
        ArgumentNullException.ThrowIfNull(logger);

        _configuration = configuration;
        _arquivos = arquivos;
        _logger = logger;
    }

    public async Task<IFonteDaCargaDne> PrepararAsync(Guid execucaoId, string versao, CancellationToken cancellationToken)
    {
        try
        {
            await CarregarAsync(execucaoId, versao, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DescartarSemFalharAsync(execucaoId).ConfigureAwait(false);
            throw;
        }

        return new DneEsquemaDeCargaFonte(
            ConnectionString(), versao, ComandosDoEsquemaDeCarga.Nome(execucaoId), () => DescartarSemFalharAsync(execucaoId));
    }

    public Task DescartarPacoteAsync(Guid execucaoId)
    {
        _arquivos.ApagarSeExistir(execucaoId);
        return Task.CompletedTask;
    }

    public async Task<int> DescartarSobrasAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> orfaos = await ListarAsync(cancellationToken).ConfigureAwait(false);
        foreach (string esquema in orfaos)
        {
            await DescartarEsquemaAsync(esquema, cancellationToken).ConfigureAwait(false);
        }

        return orfaos.Count;
    }

    private async Task CarregarAsync(Guid execucaoId, string versao, CancellationToken cancellationToken)
    {
        string esquema = ComandosDoEsquemaDeCarga.Nome(execucaoId);
        NpgsqlConnection conexao = await AbrirAsync(cancellationToken).ConfigureAwait(false);
        await using (conexao.ConfigureAwait(false))
        {
            await ExecutorDeComandosDoEsquema.ExecutarAsync(conexao, ComandosDoEsquemaDeCarga.CriarEsquema(esquema), cancellationToken).ConfigureAwait(false);
            await CopiaDoPacoteParaOEsquema
                .CopiarAsync(conexao, esquema, _arquivos.CaminhoDe(execucaoId), versao, cancellationToken)
                .ConfigureAwait(false);
        }

        _arquivos.ApagarSeExistir(execucaoId);
    }

    // Descartar nunca pode esconder a falha da carga nem falhar a publicação: o que sobrar é
    // limpo na partida seguinte.
    private async Task DescartarSemFalharAsync(Guid execucaoId)
    {
        try
        {
            await DescartarEsquemaAsync(ComandosDoEsquemaDeCarga.Nome(execucaoId), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception excecao) when (excecao is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            LogFalhaAoDescartar(_logger, execucaoId, DiagnosticoDeExcecaoSanitizado.Descrever(excecao));
        }
        finally
        {
            _arquivos.ApagarSeExistir(execucaoId);
        }
    }

    private async Task<IReadOnlyList<string>> ListarAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection conexao = await AbrirAsync(cancellationToken).ConfigureAwait(false);
        await using (conexao.ConfigureAwait(false))
        {
            NpgsqlCommand comando = new(ConsultaDosEsquemasDeCarga, conexao);
            await using (comando.ConfigureAwait(false))
            {
                return await LerNomesValidosAsync(comando, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<string>> LerNomesValidosAsync(NpgsqlCommand comando, CancellationToken cancellationToken)
    {
        List<string> nomes = [];
        NpgsqlDataReader leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using (leitor.ConfigureAwait(false))
        {
            while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string nome = leitor.GetString(0);
                if (ComandosDoEsquemaDeCarga.EhNomeValido(nome))
                {
                    nomes.Add(nome);
                }
            }
        }

        return nomes;
    }

    private async Task DescartarEsquemaAsync(string esquema, CancellationToken cancellationToken)
    {
        NpgsqlConnection conexao = await AbrirAsync(cancellationToken).ConfigureAwait(false);
        await using (conexao.ConfigureAwait(false))
        {
            await ExecutorDeComandosDoEsquema.ExecutarAsync(conexao, ComandosDoEsquemaDeCarga.DescartarEsquema(esquema), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<NpgsqlConnection> AbrirAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection conexao = new(ConnectionString());
        try
        {
            await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);
            return conexao;
        }
        catch
        {
            await conexao.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private string ConnectionString() =>
        _configuration.GetConnectionString(GeoInfrastructureRegistration.ConnectionStringName)
        ?? throw new InvalidOperationException(
            $"ConnectionStrings:{GeoInfrastructureRegistration.ConnectionStringName} não configurada para o esquema de carga do Geo.");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geo: falha ao descartar o esquema de carga da execução {ExecucaoId}; será limpo na próxima partida. Diagnóstico: {Diagnostico}")]
    private static partial void LogFalhaAoDescartar(ILogger logger, Guid execucaoId, string diagnostico);
}
