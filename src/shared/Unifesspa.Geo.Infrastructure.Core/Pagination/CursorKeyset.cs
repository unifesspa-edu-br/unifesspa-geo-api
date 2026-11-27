namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Página keyset bidirecional (ADR-0089): <see cref="Items"/> já em ordem
/// ascendente canônica por <c>Id</c> e as âncoras para emissão de
/// <c>rel="prev"</c>/<c>rel="next"</c>. Âncora nula = não há aquele lado.
/// </summary>
public sealed record CursorKeysetPage<T>(
    IReadOnlyList<T> Items,
    Guid? PrevAfterId,
    Guid? NextAfterId)
{
    /// <summary>Há página anterior (emite <c>rel="prev"</c>).</summary>
    public bool HasPrevious => PrevAfterId is not null;

    /// <summary>Há próxima página (emite <c>rel="next"</c>).</summary>
    public bool HasNext => NextAfterId is not null;
}

/// <summary>
/// Aplica paginação keyset bidirecional (ADR-0089) sobre uma query <b>já
/// filtrada</b> (os filtros de negócio, ex.: <c>q</c>/<c>tipo</c>, devem estar
/// aplicados em <paramref name="filtered"/> antes da chamada). Chave de
/// ordenação = <c>Id</c> (GUID v7 ordenável, ADR-0032).
/// </summary>
/// <remarks>
/// <para><b>Forward</b> (<see cref="PaginationDirection.Next"/>): <c>Id &gt;
/// âncora</c>, <c>ORDER BY Id ASC</c>.</para>
/// <para><b>Backward</b> (<see cref="PaginationDirection.Prev"/>): <c>Id &lt;
/// âncora</c>, <c>ORDER BY Id DESC</c>; o item-probe é cortado <b>ainda em
/// DESC</b> (é o mais antigo) e só então a lista é revertida para ascendente —
/// reverter antes de cortar omitiria o boundary.</para>
/// <para><b>Flags exatas sem <c>COUNT</c></b>: o lado navegado vem do probe
/// <c>n+1</c>; o lado oposto vem de um <c>EXISTS</c> indexado sobre a mesma
/// query base (mesmos filtros).</para>
/// </remarks>
public static class CursorKeyset
{
    public static async Task<CursorKeysetPage<T>> ApplyAsync<T>(
        IQueryable<T> filtered,
        Guid? afterId,
        int limit,
        PaginationDirection direction,
        CancellationToken cancellationToken = default)
        where T : EntityBase
    {
        ArgumentNullException.ThrowIfNull(filtered);

        return direction == PaginationDirection.Prev
            ? await ApplyBackwardAsync(filtered, afterId, limit, cancellationToken).ConfigureAwait(false)
            : await ApplyForwardAsync(filtered, afterId, limit, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<CursorKeysetPage<T>> ApplyForwardAsync<T>(
        IQueryable<T> filtered,
        Guid? afterId,
        int limit,
        CancellationToken cancellationToken)
        where T : EntityBase
    {
        IQueryable<T> forward = afterId is { } anchor
            ? filtered.Where(e => e.Id.CompareTo(anchor) > 0)
            : filtered;

        (List<T> itens, bool haProxima) = await LerComItemExcedenteAsync(
            forward.OrderBy(e => e.Id), limit, cancellationToken).ConfigureAwait(false);

        // Só há página anterior quando existe âncora (não é a primeira página).
        bool haAnterior = afterId is not null && itens.Count > 0
            && await ExisteAntesDeAsync(filtered, itens[0].Id, cancellationToken).ConfigureAwait(false);

        return ComAncoras(itens, haAnterior, haProxima);
    }

    private static async Task<CursorKeysetPage<T>> ApplyBackwardAsync<T>(
        IQueryable<T> filtered,
        Guid? afterId,
        int limit,
        CancellationToken cancellationToken)
        where T : EntityBase
    {
        IQueryable<T> backward = afterId is { } anchor
            ? filtered.Where(e => e.Id.CompareTo(anchor) < 0)
            : filtered;

        (List<T> itens, bool haAnterior) = await LerComItemExcedenteAsync(
            backward.OrderByDescending(e => e.Id), limit, cancellationToken).ConfigureAwait(false);

        // O item excedente já saiu da lista ainda em DESC (é o mais antigo); só então ela é revertida.
        itens.Reverse();
        bool haProxima = itens.Count > 0
            && await ExisteDepoisDeAsync(filtered, itens[^1].Id, cancellationToken).ConfigureAwait(false);

        return ComAncoras(itens, haAnterior, haProxima);
    }

    // As âncoras são o Id do primeiro e do último item, e só existem do lado em que há mais páginas.
    private static CursorKeysetPage<T> ComAncoras<T>(List<T> itens, bool haAnterior, bool haProxima)
        where T : EntityBase =>
        new(
            itens,
            PrevAfterId: haAnterior ? itens[0].Id : null,
            NextAfterId: haProxima ? itens[^1].Id : null);

    // Busca um item além do limite: se ele veio, há mais páginas naquele sentido, e ele não faz
    // parte da página devolvida.
    private static async Task<(List<T> Itens, bool HaMais)> LerComItemExcedenteAsync<T>(
        IQueryable<T> ordenada,
        int limite,
        CancellationToken cancellationToken)
    {
        List<T> itens = await ordenada
            .Take(limite + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool haMais = itens.Count > limite;
        if (haMais)
            itens.RemoveAt(itens.Count - 1);

        return (itens, haMais);
    }

    // O lado oposto ao navegado é confirmado por EXISTS indexado (sem COUNT) sobre a query filtrada.
    private static Task<bool> ExisteAntesDeAsync<T>(IQueryable<T> filtered, Guid id, CancellationToken cancellationToken)
        where T : EntityBase =>
        filtered.AnyAsync(e => e.Id.CompareTo(id) < 0, cancellationToken);

    private static Task<bool> ExisteDepoisDeAsync<T>(IQueryable<T> filtered, Guid id, CancellationToken cancellationToken)
        where T : EntityBase =>
        filtered.AnyAsync(e => e.Id.CompareTo(id) > 0, cancellationToken);
}
