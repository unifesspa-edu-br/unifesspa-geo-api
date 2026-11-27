namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using Microsoft.EntityFrameworkCore;

using Npgsql;

/// <summary>
/// Sessão de execução dos comandos da carga em lote sobre a conexão e a transação
/// ativas do <see cref="GeoDbContext"/>. Os comandos comuns herdam a transação do
/// contexto e o prazo configurado; a cópia binária usa a mesma conexão e, por isso,
/// a mesma transação.
/// </summary>
internal sealed class SessaoDaCargaEmLote
{
    private readonly GeoDbContext _contexto;

    private SessaoDaCargaEmLote(GeoDbContext contexto)
    {
        _contexto = contexto;
    }

    public static SessaoDaCargaEmLote DaTransacaoAtiva(GeoDbContext contexto, ConfiguracaoDaCargaEmLote configuracao)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(configuracao);

        if (contexto.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A carga em lote exige uma transação ativa no GeoDbContext.");
        }

        contexto.Database.SetCommandTimeout(configuracao.PrazoDosComandosEmSegundos);
        return new SessaoDaCargaEmLote(contexto);
    }

    public Task ExecutarAsync(string comando, CancellationToken cancellationToken) =>
        _contexto.Database.ExecuteSqlRawAsync(comando, cancellationToken);

    public async Task<long> ContarLinhasAsync(string tabela, CancellationToken cancellationToken)
    {
        string consulta = $"SELECT count(*) AS \"Value\" FROM {tabela}";
        List<long> linhas = await _contexto.Database
            .SqlQueryRaw<long>(consulta)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return linhas.Count == 0 ? 0 : linhas[0];
    }

    public async Task<long> MesclarAsync(string comandoDeMesclagem, DateTimeOffset instante, CancellationToken cancellationToken) =>
        await _contexto.Database
            .ExecuteSqlRawAsync(comandoDeMesclagem, [new NpgsqlParameter("now", instante)], cancellationToken)
            .ConfigureAwait(false);

    public Task<NpgsqlBinaryImporter> IniciarCopiaAsync(string comandoDeCopia, CancellationToken cancellationToken) =>
        ((NpgsqlConnection)_contexto.Database.GetDbConnection()).BeginBinaryImportAsync(comandoDeCopia, cancellationToken);
}
