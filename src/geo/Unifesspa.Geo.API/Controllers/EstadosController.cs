namespace Unifesspa.Geo.API.Controllers;

using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Unifesspa.Geo.API.Errors;
using Unifesspa.Geo.API.Formatting;
using Unifesspa.Geo.Application.Abstractions.Messaging;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Application.Queries.Estados;
using Unifesspa.Geo.Infrastructure.Core.Errors;
using Unifesspa.Geo.Infrastructure.Core.Formatting;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;
using Unifesspa.Geo.Infrastructure.Core.Pagination;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Endpoints autenticados de leitura de Estados (UFs) — reference data
/// (sem role administrativa, sem Idempotency-Key, carga via ETL/F3):
/// <c>GET /api/estados</c> (lista paginada por cursor) e
/// <c>GET /api/estados/{uf}</c> (detalhe pela chave natural <c>uf</c>).
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "ASP.NET Core ControllerFeatureProvider só descobre controllers public.")]
public sealed class EstadosController : ControllerBase
{
    private const string ResourceTag = "estados";

    private readonly IQueryBus _queryBus;
    private readonly IResourceLinksBuilder<EstadoDto> _linksBuilder;
    private readonly IDomainErrorMapper _mapper;

    public EstadosController(
        IQueryBus queryBus,
        IResourceLinksBuilder<EstadoDto> linksBuilder,
        IDomainErrorMapper mapper)
    {
        _queryBus = queryBus;
        _linksBuilder = linksBuilder;
        _mapper = mapper;
    }

    /// <summary>
    /// Lista as UFs vigentes, paginadas por cursor opaco bidirecional (ADR-0026 +
    /// ADR-0089). Navegação via header <c>Link</c> (RFC 5988/8288); cada item
    /// carrega seu <c>_links.self</c> resolvível para <c>GET /api/estados/{uf}</c>.
    /// </summary>
    [HttpGet("estados")]
    [VendorMediaType(Resource = "estado", Versions = [1])]
    [ProducesResponseType(typeof(IEnumerable<EstadoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status406NotAcceptable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Listar(
        [FromCursor(ResourceTag, RequireSortKey = true)] PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        ListarEstadosResult resultado = await _queryBus
            .Send(new ListarEstadosQuery(page.AfterSortKey, page.AfterId, page.Limit, page.Direction), cancellationToken)
            .ConfigureAwait(false);

        // HATEOAS Level 1 (ADR-0029 §"Coleção"): cada item carrega seu _links.self.
        EstadoDto[] comLinks = [.. resultado.Items.Select(e => e with { Links = _linksBuilder.Build(e) })];

        return await this.OkPaginatedOrdenadoAsync(
            comLinks, resultado.Anterior, resultado.Proximo, page, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Obtém um Estado pela chave natural <paramref name="uf"/> (2 letras). Formato
    /// inválido → 400 (decode no boundary, ADR-0031); bem-formado e inexistente → 404.
    /// </summary>
    [HttpGet("estados/{uf}")]
    [VendorMediaType(Resource = "estado", Versions = [1])]
    [ProducesResponseType(typeof(EstadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status406NotAcceptable)]
    public async Task<IActionResult> ObterPorUf(string uf, CancellationToken cancellationToken)
    {
        if (FormatoDaUf.Validar(uf) is { } recusa)
        {
            return Result.Failure(recusa).ToActionResult(_mapper);
        }

        return await ConsultarEstadoAsync(uf, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IActionResult> ConsultarEstadoAsync(string uf, CancellationToken cancellationToken)
    {
        EstadoDto? estado = await _queryBus
            .Send(new ObterEstadoPorUfQuery(uf), cancellationToken)
            .ConfigureAwait(false);

        if (estado is null)
        {
            return Result.Failure(ErrosDasConsultas.ErroEstadoNaoEncontrado()).ToActionResult(_mapper);
        }

        return Ok(estado with { Links = _linksBuilder.Build(estado) });
    }
}
