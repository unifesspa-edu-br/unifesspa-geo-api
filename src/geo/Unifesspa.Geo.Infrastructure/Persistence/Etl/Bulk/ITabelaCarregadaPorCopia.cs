namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using Npgsql;

/// <summary>
/// Uma tabela carregada por cópia binária: conhece seus comandos SQL, converte a fonte nas
/// linhas a copiar (contando as descartadas) e escreve cada linha na ordem das colunas da cópia.
/// </summary>
internal interface ITabelaCarregadaPorCopia<T>
{
    ComandosDaTabelaPorCopia Comandos { get; }

    IAsyncEnumerable<T> ProjetarAsync(ContadorTabela contador, CancellationToken cancellationToken);

    Task EscreverLinhaAsync(NpgsqlBinaryImporter importador, T linha, DateTimeOffset instante, CancellationToken cancellationToken);
}
