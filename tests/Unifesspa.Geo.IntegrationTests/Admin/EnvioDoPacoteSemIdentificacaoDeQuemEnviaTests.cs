namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.Net;
using System.Text.Json;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Token sem identificação de quem envia (sem a claim sub, lida pelo contexto de usuário real) não chega à regra do domínio: o contexto do usuário recusa
/// antes, e a resposta é a falha inesperada do serviço, não o erro de identificação obrigatória.
/// </summary>
public sealed class EnvioDoPacoteSemIdentificacaoDeQuemEnviaTests : IClassFixture<GeoOpenApiFactory>
{
    private readonly GeoOpenApiFactory _fabrica;

    public EnvioDoPacoteSemIdentificacaoDeQuemEnviaTests(GeoOpenApiFactory fabrica)
    {
        _fabrica = fabrica;
    }

    [Fact(DisplayName = "Envio com token sem identificação responde 500 inesperado e não chama o serviço de importação")]
    public async Task SemIdentificacao_Responde500SemChamarOServico()
    {
        ServicoDeImportacaoFalso servico = new();
        await using HostDoEnvioDoPacote host = new(
            _fabrica,
            ajustarServicos: servicos => servicos.AddSingleton<IGeoImportacaoService>(servico));
        using ByteArrayContent corpo = HostDoEnvioDoPacote.Zip(ZipsParaOEnvio.NaoEhZip());

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo, PerfilDoEnvio.AdministradorSemSubject);

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using JsonDocument problema = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        problema.RootElement.GetProperty("code").GetString().Should().Be("uniplus.internal.unexpected");
        servico.ChamadasDeIniciar.Should().Be(0);
    }
}
