namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Estado de uma tabela durante a carga por chave natural: linhas já gravadas no banco,
/// chaves já vistas nesta carga (para o último registro da fonte prevalecer) e o contador
/// do relatório. Cada gravação segue <see cref="GeoEtlUpsert.Upsert{T}"/>.
/// </summary>
internal sealed class GravacaoPorChaveNatural<T>
    where T : EntityBase
{
    private readonly DbSet<T> _tabela;
    private readonly Dictionary<string, T> _existentes;
    private readonly Dictionary<string, T> _jaVistos = new(StringComparer.Ordinal);

    private GravacaoPorChaveNatural(DbSet<T> tabela, Dictionary<string, T> existentes, ContadorTabela contador)
    {
        _tabela = tabela;
        _existentes = existentes;
        Contador = contador;
    }

    public ContadorTabela Contador { get; }

    public static async Task<GravacaoPorChaveNatural<T>> AbrirAsync(
        DbSet<T> tabela,
        Func<T, string> chaveNatural,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        ArgumentNullException.ThrowIfNull(chaveNatural);
        ArgumentNullException.ThrowIfNull(contador);

        Dictionary<string, T> existentes =
            await tabela.ToDictionaryAsync(chaveNatural, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        return new GravacaoPorChaveNatural<T>(tabela, existentes, contador);
    }

    public T? Gravar(string chave, Func<Result<T>> criar, Func<T, Result> atualizar) =>
        GeoEtlUpsert.Upsert(chave, _jaVistos, _existentes, criar, atualizar, _tabela, Contador);
}
