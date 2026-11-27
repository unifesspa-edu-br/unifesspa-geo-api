namespace Unifesspa.Geo.API.Controllers;

using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Unifesspa.Geo.API.Errors;
using Unifesspa.Geo.API.Formatting;
using Unifesspa.Geo.Application.Abstractions.Messaging;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Application.Queries.Cidades;
using Unifesspa.Geo.Infrastructure.Core.Errors;
using Unifesspa.Geo.Infrastructure.Core.Formatting;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;
using Unifesspa.Geo.Infrastructure.Core.Pagination;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Endpoints autenticados de leitura de Cidades — reference data
/// (sem role administrativa, sem Idempotency-Key, carga via ETL/F3):
/// <c>GET /api/cidades?uf=&amp;q=</c> (lista paginada com filtro por UF e busca
/// textual) e <c>GET /api/cidades/{codigoIbge}</c> (detalhe pela chave natural).
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "ASP.NET Core ControllerFeatureProvider só descobre controllers public.")]
public sealed class CidadesController : ControllerBase
{
    private const string ResourceTag = "cidades";

    private readonly IQueryBus _queryBus;
    private readonly IResourceLinksBuilder<CidadeResumoDto> _resumoLinks;
    private readonly IResourceLinksBuilder<CidadeDetalheDto> _detalheLinks;
    private readonly IDomainErrorMapper _mapper;

    public CidadesController(
        IQueryBus queryBus,
        IResourceLinksBuilder<CidadeResumoDto> resumoLinks,
        IResourceLinksBuilder<CidadeDetalheDto> detalheLinks,
        IDomainErrorMapper mapper)
    {
        _mapper = mapper;
        _queryBus = queryBus;
        _resumoLinks = resumoLinks;
        _detalheLinks = detalheLinks;
    }

    /// <summary>
    /// Lista as Cidades vigentes, paginadas por cursor opaco bidirecional (ADR-0026 +
    /// ADR-0089), com filtros opcionais: <c>uf</c> (filtro por UF) e <c>q</c> (busca
    /// textual acento/caixa-insensível sobre o nome). Os filtros viajam como query
    /// params e combinam com o cursor — o cliente reanexa-os a cada página ao seguir
    /// o <c>cursor</c> do header <c>Link</c>.
    /// </summary>
    [HttpGet("cidades")]
    [VendorMediaType(Resource = "cidade", Versions = [1])]
    [ProducesResponseType(typeof(IEnumerable<CidadeResumoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status406NotAcceptable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Listar(
        [FromCursor(ResourceTag, RequireSortKey = true)] PageRequest page,
        [FromQuery(Name = "uf")] string? uf,
        [FromQuery(Name = "q")] string? q,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (TermoDeBusca.Validar(q) is { } recusa)
        {
            return Result.Failure(recusa).ToActionResult(_mapper);
        }

        return await ListarPaginaAsync(page, uf, q, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IActionResult> ListarPaginaAsync(
        PageRequest page,
        string? uf,
        string? q,
        CancellationToken cancellationToken)
    {
        ListarCidadesResult resultado = await _queryBus
            .Send(new ListarCidadesQuery(page.AfterSortKey, page.AfterId, page.Limit, page.Direction, uf, q), cancellationToken)
            .ConfigureAwait(false);

        // HATEOAS Level 1 (ADR-0029 §"Coleção"): cada item carrega seu _links.self.
        CidadeResumoDto[] comLinks = [.. resultado.Items.Select(c => c with { Links = _resumoLinks.Build(c) })];

        return await this.OkPaginatedOrdenadoAsync(
            comLinks, resultado.Anterior, resultado.Proximo, page, ResourceTag,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Obtém uma Cidade pela chave natural <paramref name="codigoIbge"/> (7 dígitos),
    /// com territorial embutido + indicador 1:1. Formato inválido → 400 (decode no
    /// boundary, ADR-0031); bem-formado e inexistente → 404.
    /// </summary>
    [HttpGet("cidades/{codigoIbge}")]
    [VendorMediaType(Resource = "cidade", Versions = [1])]
    [ProducesResponseType(typeof(CidadeDetalheDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status406NotAcceptable)]
    public async Task<IActionResult> ObterPorCodigoIbge(string codigoIbge, CancellationToken cancellationToken)
    {
        if (FormatoDoCodigoIbge.Validar(codigoIbge) is { } recusa)
        {
            return Result.Failure(recusa).ToActionResult(_mapper);
        }

        return await ConsultarCidadeAsync(codigoIbge, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IActionResult> ConsultarCidadeAsync(string codigoIbge, CancellationToken cancellationToken)
    {
        CidadeDetalheDto? cidade = await _queryBus
            .Send(new ObterCidadePorCodigoIbgeQuery(codigoIbge), cancellationToken)
            .ConfigureAwait(false);

        if (cidade is null)
        {
            return Result.Failure(ErrosDasConsultas.ErroCidadeNaoEncontrada()).ToActionResult(_mapper);
        }

        return Ok(cidade with { Links = _detalheLinks.Build(cidade) });
    }
}
