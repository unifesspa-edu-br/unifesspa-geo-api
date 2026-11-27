namespace Unifesspa.Geo.IntegrationTests.Errors;

using System.Text.Json;

using AwesomeAssertions;

using FluentValidation;
using FluentValidation.Results;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;

using Unifesspa.Geo.Infrastructure.Core.Authentication;
using Unifesspa.Geo.Infrastructure.Core.Errors;
using Unifesspa.Geo.Infrastructure.Core.Formatting;
using Unifesspa.Geo.Infrastructure.Core.Middleware;
using Unifesspa.Geo.Kernel.Results;

/// <summary>Em cada emissor de erro, o campo type é a base configurada seguida do código da resposta.</summary>
public sealed class EnderecoDoErroNasRespostasTests : IDisposable
{
    private readonly ServiceProvider _servicos = ServicosDeErroParaTeste.Criar();

    public void Dispose() => _servicos.Dispose();

    [Fact(DisplayName = "Erro de domínio mapeado aponta o type para a base mais o código")]
    public void ResultadoDeDominio_TypeEBaseMaisCodigo()
    {
        Result falha = Result.Failure(new DomainError("uniplus.validacao", "A requisição contém erros de validação."));

        IActionResult resposta = falha.ToActionResult(_servicos.GetRequiredService<IDomainErrorMapper>());

        ProblemDetails problema = (ProblemDetails)((ObjectResult)resposta).Value!;
        problema.Type.Should().Be(ServicosDeErroParaTeste.BaseDoPortal + "uniplus.validacao");
        problema.Extensions["code"].Should().Be("uniplus.validacao");
    }

    [Fact(DisplayName = "401 da autenticação aponta o type para a base mais o código")]
    public async Task NaoAutenticado_TypeEBaseMaisCodigo()
    {
        DefaultHttpContext contexto = NovoContextoHttp();

        await AuthenticationProblemDetailsWriter.WriteUnauthorizedAsync(contexto);

        await ConferirRespostaAsync(contexto, StatusCodes.Status401Unauthorized, "uniplus.auth.unauthorized");
    }

    [Fact(DisplayName = "403 da autorização aponta o type para a base mais o código")]
    public async Task AcessoNegado_TypeEBaseMaisCodigo()
    {
        DefaultHttpContext contexto = NovoContextoHttp();

        await AuthenticationProblemDetailsWriter.WriteForbiddenAsync(contexto);

        await ConferirRespostaAsync(contexto, StatusCodes.Status403Forbidden, "uniplus.auth.forbidden");
    }

    [Fact(DisplayName = "406 de versão de mídia aponta o type para a base mais o código")]
    public void VersaoDeMidiaNaoSuportada_TypeEBaseMaisCodigo()
    {
        DefaultHttpContext http = NovoContextoHttp();
        http.Request.Headers[HeaderNames.Accept] = "application/vnd.uniplus.estado.v9+json";
        ActionExecutingContext contexto = new(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), [], new Dictionary<string, object?>(StringComparer.Ordinal), controller: new object());

        new VendorMediaTypeAttribute { Resource = "estado", Versions = [1] }.OnActionExecuting(contexto);

        ObjectResult resultado = contexto.Result.Should().BeOfType<ObjectResult>().Subject;
        resultado.StatusCode.Should().Be(StatusCodes.Status406NotAcceptable);
        ProblemDetails problema = (ProblemDetails)resultado.Value!;
        problema.Type.Should().Be(ServicosDeErroParaTeste.BaseDoPortal + "uniplus.contract.versao_nao_suportada");
    }

    [Fact(DisplayName = "500 de falha inesperada aponta o type para a base mais o código")]
    public async Task FalhaInesperada_TypeEBaseMaisCodigo()
    {
        DefaultHttpContext contexto = NovoContextoHttp();
        GlobalExceptionMiddleware middleware = NovoMiddleware(static _ => throw new InvalidOperationException("falha do teste"));

        await middleware.InvokeAsync(contexto);

        await ConferirRespostaAsync(contexto, StatusCodes.Status500InternalServerError, "uniplus.internal.unexpected");
    }

    [Fact(DisplayName = "422 de validação aponta o type para a base mais o código")]
    public async Task ErroDeValidacao_TypeEBaseMaisCodigo()
    {
        DefaultHttpContext contexto = NovoContextoHttp();
        ValidationException falha = new([new ValidationFailure("campo", "Campo obrigatório.")]);
        GlobalExceptionMiddleware middleware = NovoMiddleware(_ => throw falha);

        await middleware.InvokeAsync(contexto);

        await ConferirRespostaAsync(contexto, StatusCodes.Status422UnprocessableEntity, "uniplus.validacao");
    }

    private DefaultHttpContext NovoContextoHttp()
    {
        DefaultHttpContext contexto = new() { RequestServices = _servicos };
        contexto.Response.Body = new MemoryStream();
        return contexto;
    }

    private GlobalExceptionMiddleware NovoMiddleware(RequestDelegate proximo) =>
        new(proximo, NullLogger<GlobalExceptionMiddleware>.Instance, _servicos.GetRequiredService<ProblemTypeUriFactory>());

    private static async Task ConferirRespostaAsync(HttpContext contexto, int status, string codigo)
    {
        contexto.Response.StatusCode.Should().Be(status);
        contexto.Response.Body.Position = 0;
        using JsonDocument corpo = await JsonDocument.ParseAsync(contexto.Response.Body);
        corpo.RootElement.GetProperty("type").GetString().Should().Be(ServicosDeErroParaTeste.BaseDoPortal + codigo);
        corpo.RootElement.GetProperty("code").GetString().Should().Be(codigo);
    }
}
