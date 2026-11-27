namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

using MR.EntityFrameworkCore.KeysetPagination;

using Unifesspa.Geo.Kernel.Domain.Entities;

/// <summary>
/// A ordem em que uma listagem é paginada por keyset ordenado (ADR-0094): as colunas que o motor
/// de seek ordena, a chave de ordenação de um item e a montagem da âncora de continuação. As três
/// partes descrevem a mesma ordem e por isso viajam juntas.
/// </summary>
/// <typeparam name="T">Entidade listada.</typeparam>
public sealed class OrdenacaoDaListagem<T>
    where T : EntityBase
{
    private readonly Action<KeysetPaginationBuilder<T>> _colunas;
    private readonly Func<T, string> _chaveDoItem;
    private readonly Func<string, Guid, object> _ancora;

    /// <param name="colunas">Colunas do motor, ex.: <c>b =&gt; b.Ascending(x =&gt; x.NomeOrdenacao).Ascending(x =&gt; x.Id)</c>.</param>
    /// <param name="chaveDoItem">Chave de ordenação de um item, nunca nula (ADR-0095).</param>
    /// <param name="ancora">Objeto de referência do motor para a chave de ordenação e o <c>Id</c> do cursor.</param>
    public OrdenacaoDaListagem(
        Action<KeysetPaginationBuilder<T>> colunas,
        Func<T, string> chaveDoItem,
        Func<string, Guid, object> ancora)
    {
        ArgumentNullException.ThrowIfNull(colunas);
        ArgumentNullException.ThrowIfNull(chaveDoItem);
        ArgumentNullException.ThrowIfNull(ancora);

        _colunas = colunas;
        _chaveDoItem = chaveDoItem;
        _ancora = ancora;
    }

    internal void ConfigurarColunas(KeysetPaginationBuilder<T> builder) => _colunas(builder);

    // A âncora é completa (chave de ordenação e Id) ou não existe: sem ela a listagem está na
    // primeira página.
    internal object? AncoraDaContinuacao(string? chave, Guid? id) =>
        chave is not null && id is { } desempate ? _ancora(chave, desempate) : null;

    internal (string SortKey, Guid Id) AncoraDoItem(T item) => (_chaveDoItem(item), item.Id);
}
