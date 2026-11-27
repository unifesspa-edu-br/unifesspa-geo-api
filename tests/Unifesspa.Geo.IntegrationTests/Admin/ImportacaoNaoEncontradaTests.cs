namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.Net;
using System.Text.Json;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>Importação inexistente responde 404 com o código próprio e o endereço do tipo do portal.</summary>
public sealed class ImportacaoNaoEncontradaTests : IClassFixture<GeoOpenApiFactory>
{
    private const string BaseDoPortal = "https://unifesspa-edu-br.github.io/uniplus-developers/erros/";

    private readonly GeoOpenApiFactory _fabrica;

    public ImportacaoNaoEncontradaTests(GeoOpenApiFactory fabrica)
    {
        _fabrica = fabrica;
    }

    [Fact(DisplayName = "Consulta de importação inexistente responde 404 com código, tipo e detalhe sem o identificador")]
    public async Task ImportacaoInexistente_Responde404ComCodigo()
    {
        Guid id = Guid.NewGuid();
        await using HostDoEnvioDoPacote host = new(
            _fabrica,
            ajustarServicos: servicos => servicos.AddSingleton<IGeoImportacaoService>(new ServicoDeImportacaoFalso()));

        using HttpResponseMessage resposta = await host.ObterAsync(new Uri($"{HostDoEnvioDoPacote.Rota}/{id}", UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using JsonDocument problema = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        const string codigo = "uniplus.geo.importacao.nao_encontrada";
        problema.RootElement.GetProperty("code").GetString().Should().Be(codigo);
        problema.RootElement.GetProperty("type").GetString().Should().Be(BaseDoPortal + codigo);
        problema.RootElement.GetProperty("detail").GetString().Should().NotContain(id.ToString());
    }
}
