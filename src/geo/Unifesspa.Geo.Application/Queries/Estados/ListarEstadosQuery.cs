namespace Unifesspa.Geo.Application.Queries.Estados;

using Unifesspa.Geo.Application.Abstractions.Messaging;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Lista os Estados (UFs) vigentes paginados por cursor bidirecional (ADR-0026 +
/// ADR-0089). Os parâmetros já chegam decodificados — o controller decifra o
/// cursor opaco no boundary (ADR-0031) antes de despachar, mantendo Application
/// independente de Infrastructure.Core.
/// </summary>
/// <param name="AfterSortKey">Chave de ordenação (nome) da âncora; par com <paramref name="AfterId"/> (ADR-0094).</param>
/// <param name="AfterId">Id de desempate da âncora; <see langword="null"/> retorna a primeira janela.</param>
/// <param name="Limit">Tamanho máximo da página.</param>
/// <param name="Direction">Direção de navegação (<c>Next</c>/<c>Prev</c>).</param>
public sealed record ListarEstadosQuery(
    string? AfterSortKey,
    Guid? AfterId,
    int Limit,
    PaginationDirection Direction) : IQuery<ListarEstadosResult>;
