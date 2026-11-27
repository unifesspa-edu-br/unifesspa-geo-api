namespace Unifesspa.Geo.IntegrationTests.Admin;

using AwesomeAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Unifesspa.Geo.API.Controllers;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Passos do envio no controller, sem banco: repasse do corpo e do administrador ao registro e
/// resposta 202 ou 200 com <c>Location</c>, ou o erro mapeado em <c>ProblemDetails</c>.
/// </summary>
public sealed class EnvioDoPacoteNoControllerTests
{
    private readonly ServicoDeImportacaoFalso _servico = new();
    private readonly Guid _execucao = Guid.NewGuid();

    [Fact(DisplayName = "Pacote novo: repassa o corpo e o administrador ao registro e responde 202 com Location")]
    public async Task PacoteNovo_Responde202ComLocation()
    {
        PrepararExecucao(DesfechoDoEnvioDoPacote.CargaEnfileirada);
        using MemoryStream corpo = new([1, 2, 3]);

        IActionResult resposta = await EnviarAsync(corpo, new DefaultHttpContext());

        resposta.Should().BeOfType<AcceptedResult>().Which.Location.Should().Be(LinksDeImportacaoParaTeste.Endereco(_execucao));
        _servico.CorpoRecebido.Should().BeSameAs(corpo);
        _servico.DisparadoPor.Should().Be(UsuarioAdministradorParaTeste.Identificador);
    }

    [Fact(DisplayName = "Pacote já publicado: responde 200 com Location da execução vigente")]
    public async Task PacoteJaPublicado_Responde200ComLocation()
    {
        PrepararExecucao(DesfechoDoEnvioDoPacote.JaPublicado);
        DefaultHttpContext http = new();

        IActionResult resposta = await EnviarAsync(new MemoryStream(), http);

        resposta.Should().BeOfType<OkObjectResult>();
        http.Response.Headers.Location.ToString().Should().Be(LinksDeImportacaoParaTeste.Endereco(_execucao));
    }

    [Theory(DisplayName = "Recusa do registro vira ProblemDetails com o status mapeado")]
    [InlineData(GeoImportacaoErrorCodes.ImportacaoEmAndamento, StatusCodes.Status409Conflict)]
    [InlineData(GeoImportacaoErrorCodes.VersaoNaoProgressiva, StatusCodes.Status409Conflict)]
    [InlineData(GeoImportacaoErrorCodes.PacoteInvalido, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(GeoImportacaoErrorCodes.PacoteAcimaDoLimite, StatusCodes.Status413PayloadTooLarge)]
    public async Task RecusaDoRegistro_ViraProblemDetails(string codigo, int status)
    {
        _servico.ResultadoDoEnvio = Result<EnvioDoPacoteRegistrado>.Failure(new DomainError(codigo, "recusa do teste"));

        IActionResult resposta = await EnviarAsync(new MemoryStream(), new DefaultHttpContext());

        resposta.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(status);
    }

    [Fact(DisplayName = "Execução que não pode ser relida depois do registro responde 500")]
    public async Task ExecucaoNaoRelida_Responde500()
    {
        IActionResult resposta = await EnviarAsync(new MemoryStream(), new DefaultHttpContext());

        resposta.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    private void PrepararExecucao(DesfechoDoEnvioDoPacote desfecho)
    {
        _servico.ResultadoDoEnvio = Result<EnvioDoPacoteRegistrado>.Success(new(_execucao, desfecho));
        _servico.Execucao = new ImportacaoGeoDto(
            _execucao,
            "202610",
            "EmAndamento",
            DateTimeOffset.UnixEpoch,
            null,
            UsuarioAdministradorParaTeste.Identificador,
            null,
            new PacoteDaImportacaoDto(new string('a', 64), 10),
            null);
    }

    private async Task<IActionResult> EnviarAsync(Stream corpo, DefaultHttpContext http)
    {
        http.Request.Body = corpo;
        GeoImportacoesController controller = new(
            _servico,
            new MapeadorDeErrosParaTeste(),
            new LinksDeImportacaoParaTeste(),
            new UsuarioAdministradorParaTeste())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        return await controller.Enviar(CancellationToken.None);
    }
}
