namespace Unifesspa.Geo.API.Controllers;

using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Unifesspa.Geo.API.Errors;
using Unifesspa.Geo.API.Filters;
using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.Abstractions.Authentication;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Core.Errors;
using Unifesspa.Geo.Infrastructure.Core.Hateoas;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Envio do pacote dos Correios e acompanhamento da atualização da base, sob o prefixo
/// <c>/api/admin/...</c> e restritos ao role <c>plataforma-admin</c>.
/// </summary>
/// <remarks>
/// Não usa chave de idempotência de requisição: o resumo SHA-256 do pacote identifica o envio e a
/// trava da carga recusa o envio simultâneo.
/// </remarks>
[ApiController]
[Route("api")]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "ASP.NET Core ControllerFeatureProvider só descobre controllers public.")]
public sealed class GeoImportacoesController : ControllerBase
{
    private readonly IGeoImportacaoService _servico;
    private readonly IDomainErrorMapper _mapper;
    private readonly IResourceLinksBuilder<ImportacaoGeoDto> _linksBuilder;
    private readonly IRequiredUserContext _userContext;

    public GeoImportacoesController(
        IGeoImportacaoService servico,
        IDomainErrorMapper mapper,
        IResourceLinksBuilder<ImportacaoGeoDto> linksBuilder,
        IRequiredUserContext userContext)
    {
        _servico = servico;
        _mapper = mapper;
        _linksBuilder = linksBuilder;
        _userContext = userContext;
    }

    /// <summary>
    /// Recebe o pacote dos Correios (<c>application/zip</c> no corpo) e inicia a carga em segundo
    /// plano. Restrito a <c>plataforma-admin</c>. <c>202</c> com <c>Location</c> da nova execução;
    /// <c>200</c> com <c>Location</c> da execução vigente quando o pacote é o já publicado.
    /// </summary>
    [HttpPost("admin/geo/importacoes")]
    [Authorize(Roles = "plataforma-admin")]
    [ServiceFilter(typeof(ValidacaoDoEnvioDoPacoteFilter), Order = 1)]
    [ServiceFilter(typeof(UmEnvioPorVezFilter), Order = 2)]
    [ProducesResponseType(typeof(ImportacaoGeoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ImportacaoGeoDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Enviar(CancellationToken cancellationToken)
    {
        string disparadoPor = _userContext.UserId;

        Result<EnvioDoPacoteRegistrado> envio = await _servico
            .IniciarAsync(Request.Body, disparadoPor, cancellationToken)
            .ConfigureAwait(false);

        return envio.IsFailure
            ? envio.ToActionResult(_mapper)
            : await RespostaDoEnvioAsync(envio.Value!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Obtém o estado e o relatório de uma execução do ETL pelo Id. Restrito a
    /// <c>plataforma-admin</c>.
    /// </summary>
    [HttpGet("admin/geo/importacoes/{id:guid}")]
    [Authorize(Roles = "plataforma-admin")]
    [ProducesResponseType(typeof(ImportacaoGeoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Obter(Guid id, CancellationToken cancellationToken)
    {
        ImportacaoGeoDto? dto = await _servico.ObterAsync(id, cancellationToken).ConfigureAwait(false);
        if (dto is null)
        {
            return Result.Failure(ErrosDasConsultas.ErroImportacaoNaoEncontrada()).ToActionResult(_mapper);
        }

        ImportacaoGeoDto comLinks = dto with { Links = _linksBuilder.Build(dto) };
        return Ok(comLinks);
    }

    private async Task<IActionResult> RespostaDoEnvioAsync(EnvioDoPacoteRegistrado envio, CancellationToken cancellationToken)
    {
        ImportacaoGeoDto dto = await ReleituraDaExecucaoRegistradaAsync(envio, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, string> links = _linksBuilder.Build(dto);
        ImportacaoGeoDto comLinks = dto with { Links = links };
        if (envio.Desfecho == DesfechoDoEnvioDoPacote.CargaEnfileirada)
        {
            return Accepted(new Uri(links["self"], UriKind.Relative), comLinks);
        }

        Response.Headers.Location = links["self"];
        return Ok(comLinks);
    }

    private async Task<ImportacaoGeoDto> ReleituraDaExecucaoRegistradaAsync(
        EnvioDoPacoteRegistrado envio,
        CancellationToken cancellationToken) =>
        await _servico.ObterAsync(envio.ExecucaoId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(
            $"A execução {envio.ExecucaoId} acabou de ser registrada e não foi encontrada para montar a resposta do envio.");
}
