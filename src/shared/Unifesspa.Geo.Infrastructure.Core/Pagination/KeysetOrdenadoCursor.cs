namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

using Microsoft.EntityFrameworkCore;

using MR.EntityFrameworkCore.KeysetPagination;

using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Página keyset ordenada (ADR-0094): <see cref="Items"/> em ordem ascendente
/// canônica pela chave de ordenação + <c>Id</c>, e as âncoras
/// <c>(SortKey, Id)</c> para emitir <c>rel="prev"</c>/<c>rel="next"</c>. Âncora
/// nula = não há aquele lado.
/// </summary>
public sealed record KeysetOrdenadoPage<T>(
    IReadOnlyList<T> Items,
    (string SortKey, Guid Id)? Anterior,
    (string SortKey, Guid Id)? Proximo);

/// <summary>
/// Aplica paginação keyset multi-coluna ordenada (ADR-0094) sobre uma query <b>já
/// filtrada</b>, usando o motor de seek da <c>MR.EntityFrameworkCore.KeysetPagination</c>
/// sob a nossa camada de cursor opaco (ADR-0026). A chave de ordenação é definida pelo
/// chamador (ex.: <c>b =&gt; b.Ascending(x =&gt; x.NomeOrdenacao).Ascending(x =&gt; x.Id)</c>),
/// que também fornece como extrair a sort key de um item e como montar a âncora
/// (<see cref="OrdenacaoDaListagem{T}"/>) — assim o
/// <c>Id</c>-only (<see cref="CursorKeyset"/>) segue intocado para quem não ordena.
/// </summary>
/// <remarks>
/// <para><b>Chave não-nula (ADR-0095):</b> a MR não suporta coluna nullable no keyset
/// (NULL invalida o <c>WHERE</c> do seek → zero resultados). O chamador deve coalescer a
/// sort key para não-nulo antes de passá-la ao builder; o índice correspondente deve casar
/// a mesma expressão/coluna de ordenação.</para>
/// <para><b>Flags bidirecionais (ADR-0089):</b> <c>EnsureCorrectOrder</c> restaura a ordem
/// ascendente na navegação <c>Backward</c>; <c>HasPreviousAsync</c>/<c>HasNextAsync</c>
/// resolvem os lados por <c>EXISTS</c> indexado (sem <c>COUNT</c>).</para>
/// </remarks>
public static class KeysetOrdenadoCursor
{
    public static async Task<KeysetOrdenadoPage<T>> ApplyAsync<T>(
        IQueryable<T> filtered,
        OrdenacaoDaListagem<T> ordenacao,
        string? afterSortKey,
        Guid? afterId,
        int limit,
        PaginationDirection direction,
        CancellationToken cancellationToken = default)
        where T : EntityBase
    {
        ArgumentNullException.ThrowIfNull(filtered);
        ArgumentNullException.ThrowIfNull(ordenacao);

        KeysetPaginationContext<T> contexto = filtered.KeysetPaginate(
            ordenacao.ConfigurarColunas,
            DirecaoDoMotor(direction),
            ordenacao.AncoraDaContinuacao(afterSortKey, afterId));

        List<T> itens = await LerItensAsync(contexto, limit, cancellationToken).ConfigureAwait(false);
        return await MontarPaginaAsync(contexto, itens, ordenacao).ConfigureAwait(false);
    }

    private static KeysetPaginationDirection DirecaoDoMotor(PaginationDirection direcao) =>
        direcao == PaginationDirection.Prev
            ? KeysetPaginationDirection.Backward
            : KeysetPaginationDirection.Forward;

    private static async Task<List<T>> LerItensAsync<T>(
        KeysetPaginationContext<T> contexto,
        int limite,
        CancellationToken cancellationToken)
        where T : EntityBase
    {
        List<T> itens = await contexto.Query
            .Take(limite)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Backward devolve em ordem inversa; restaura a ordem ascendente canônica.
        contexto.EnsureCorrectOrder(itens);
        return itens;
    }

    private static async Task<KeysetOrdenadoPage<T>> MontarPaginaAsync<T>(
        KeysetPaginationContext<T> contexto,
        List<T> itens,
        OrdenacaoDaListagem<T> ordenacao)
        where T : EntityBase
    {
        if (itens.Count == 0)
            return new KeysetOrdenadoPage<T>(itens, Anterior: null, Proximo: null);

        // Flags exatas por EXISTS indexado (sem COUNT) — ADR-0089.
        bool temAnterior = await contexto.HasPreviousAsync(itens).ConfigureAwait(false);
        bool temProximo = await contexto.HasNextAsync(itens).ConfigureAwait(false);

        return new KeysetOrdenadoPage<T>(
            itens,
            Anterior: temAnterior ? ordenacao.AncoraDoItem(itens[0]) : null,
            Proximo: temProximo ? ordenacao.AncoraDoItem(itens[^1]) : null);
    }
}
