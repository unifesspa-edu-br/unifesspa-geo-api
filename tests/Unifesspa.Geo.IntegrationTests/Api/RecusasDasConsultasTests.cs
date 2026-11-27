namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using AwesomeAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Unifesspa.Geo.Application.Abstractions.Messaging;
using Unifesspa.Geo.IntegrationTests.Fixtures.Authentication;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Cada recusa das consultas de CEP, cidade, estado e busca devolve o código próprio, o endereço
/// do tipo a partir da configuração e um detalhe que não repete o valor enviado.
/// </summary>
public sealed class RecusasDasConsultasTests : IClassFixture<GeoOpenApiFactory>
{
    private const string BaseDoPortal = "https://unifesspa-edu-br.github.io/uniplus-developers/erros/";
    private static readonly string TermoLongo = new('k', 257);

    private readonly WebApplicationFactory<Program> _fabrica;

    public RecusasDasConsultasTests(GeoOpenApiFactory origem)
    {
        ArgumentNullException.ThrowIfNull(origem);
        _fabrica = origem.WithWebHostBuilder(static builder => builder.ConfigureTestServices(static servicos =>
        {
            servicos.RemoveAll<IQueryBus>();
            servicos.AddSingleton<IQueryBus>(new ConsultasSemResultadoParaTeste());
        }));
    }

    [Theory(DisplayName = "Recusa da consulta devolve status, código e endereço do tipo, sem repetir o valor enviado")]
    [InlineData("/api/cep/9abc", "9abc", 400, "uniplus.geo.cep.formato_invalido")]
    [InlineData("/api/cep/01001000", "01001000", 404, "uniplus.geo.cep.nao_encontrado")]
    [InlineData("/api/cidades/78x", "78x", 400, "uniplus.geo.cidade.codigo_ibge_invalido")]
    [InlineData("/api/cidades/1500602", "1500602", 404, "uniplus.geo.cidade.nao_encontrada")]
    [InlineData("/api/cidades/78x/distritos", "78x", 400, "uniplus.geo.cidade.codigo_ibge_invalido")]
    [InlineData("/api/cidades/78x/bairros", "78x", 400, "uniplus.geo.cidade.codigo_ibge_invalido")]
    [InlineData("/api/cidades/78x/logradouros", "78x", 400, "uniplus.geo.cidade.codigo_ibge_invalido")]
    [InlineData("/api/cidades/1500602/distritos", "1500602", 404, "uniplus.geo.cidade.nao_encontrada")]
    [InlineData("/api/cidades/1500602/bairros", "1500602", 404, "uniplus.geo.cidade.nao_encontrada")]
    [InlineData("/api/cidades/1500602/logradouros", "1500602", 404, "uniplus.geo.cidade.nao_encontrada")]
    [InlineData("/api/estados/ZQX", "ZQX", 400, "uniplus.geo.estado.uf_invalida")]
    [InlineData("/api/estados/ZZ", "ZZ", 404, "uniplus.geo.estado.nao_encontrado")]
    public async Task Recusa_TemStatusCodigoETipo(string rota, string valorEnviado, int status, string codigo)
    {
        using JsonDocument corpo = await ConsultarAsync(rota, status);

        ConferirProblema(corpo, codigo, valorEnviado);
    }

    [Theory(DisplayName = "Consulta por proximidade recusada devolve o código do parâmetro, sem repetir o valor enviado")]
    [InlineData("/api/cidades/proximas?long=-49.13&raioKm=10", "uniplus.geo.proximidade.parametros_ausentes", "-49.13")]
    [InlineData("/api/cidades/proximas?lat=91.5&long=-49.13&raioKm=10", "uniplus.geo.proximidade.latitude_invalida", "91.5")]
    [InlineData("/api/cidades/proximas?lat=-5.35&long=-181.5&raioKm=10", "uniplus.geo.proximidade.longitude_invalida", "181.5")]
    [InlineData("/api/cidades/proximas?lat=-5.35&long=-49.13&raioKm=777", "uniplus.geo.proximidade.raio_invalido", "777")]
    [InlineData("/api/logradouros/proximos?lat=-5.35&long=-49.13&raioKm=10&limit=-7", "uniplus.geo.proximidade.limite_invalido", "-7")]
    public async Task Proximidade_RecusaTemCodigoETipo(string rota, string codigo, string valorEnviado)
    {
        using JsonDocument corpo = await ConsultarAsync(rota, 400);

        ConferirProblema(corpo, codigo, valorEnviado);
    }

    [Theory(DisplayName = "Valor que não converte ao tipo do parâmetro devolve 400 comum, citando só o nome do parâmetro")]
    [InlineData("/api/cidades/proximas?lat=abc&long=-49.13&raioKm=10", "abc", "o parâmetro 'lat'")]
    [InlineData("/api/cidades/proximas?lat=-5.35&long=-49.13&raioKm=10&limit=xyz", "xyz", "o parâmetro 'limit'")]
    [InlineData("/api/logradouros/proximos?lat=abc&long=def&raioKm=10", "abc", "os parâmetros 'lat', 'long'")]
    public async Task ValorQueNaoConverte_Devolve400Comum(string rota, string valorEnviado, string trechoDoDetalhe)
    {
        using JsonDocument corpo = await ConsultarAsync(rota, 400);

        ConferirProblema(corpo, "uniplus.requisicao.valor_invalido", valorEnviado);
        corpo.RootElement.GetProperty("detail").GetString().Should().Contain(trechoDoDetalhe);
    }

    [Theory(DisplayName = "Erro de cursor continua respondendo com o código do cursor, antes do 400 comum")]
    [InlineData("/api/estados?limit=0", 422, "uniplus.cursor.limit_invalido", "limit=0")]
    [InlineData("/api/estados?cursor=lixo", 400, "uniplus.cursor.invalido", "lixo")]
    public async Task ErroDeCursor_MantemOCodigoDoCursor(string rota, int status, string codigo, string valorEnviado)
    {
        using JsonDocument corpo = await ConsultarAsync(rota, status);

        ConferirProblema(corpo, codigo, valorEnviado);
    }

    [Theory(DisplayName = "Termo de busca acima do máximo devolve 400 com código próprio, sem repetir o termo")]
    [InlineData("/api/cidades?q=")]
    [InlineData("/api/cidades/1500602/bairros?q=")]
    [InlineData("/api/cidades/1500602/logradouros?q=")]
    public async Task TermoLongo_Devolve400(string rotaSemTermo)
    {
        using JsonDocument corpo = await ConsultarAsync(rotaSemTermo + TermoLongo, 400);

        ConferirProblema(corpo, "uniplus.geo.busca.termo_muito_longo", "kkkk");
    }

    private async Task<JsonDocument> ConsultarAsync(string rota, int statusEsperado)
    {
        using HttpClient cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.AuthorizationScheme, TestAuthHandler.TokenValue);

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri(rota, UriKind.Relative));

        ((int)resposta.StatusCode).Should().Be(statusEsperado);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
    }

    private static void ConferirProblema(JsonDocument documento, string codigo, string valorEnviado)
    {
        JsonElement raiz = documento.RootElement;
        raiz.GetProperty("code").GetString().Should().Be(codigo);
        raiz.GetProperty("type").GetString().Should().Be(BaseDoPortal + codigo);
        raiz.GetProperty("title").GetString().Should().NotContain(valorEnviado);
        raiz.GetProperty("detail").GetString().Should().NotContain(valorEnviado);
    }
}
