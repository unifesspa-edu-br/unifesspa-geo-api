namespace Unifesspa.Geo.IntegrationTests.Formato;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

using AwesomeAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.IntegrationTests.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.IntegrationTests.Paginacao;

/// <summary>
/// A negociação do cabeçalho <c>Accept</c> pela versão de mídia do recurso: a versão escolhida, as
/// exclusões por q=0, a recusa 406 e os cabeçalhos da resposta aceita. O filtro roda com versões
/// 1, 2 e 3 sobre uma requisição montada à mão; a rota de estados confere o resultado pela API.
/// </summary>
public sealed class NegociacaoDaVersaoDeMidiaTests : IClassFixture<GeoOpenApiFactory>, IDisposable
{
    private const string V1 = "application/vnd.uniplus.estado.v1+json";
    private const string V2 = "application/vnd.uniplus.estado.v2+json";
    private const string V3 = "application/vnd.uniplus.estado.v3+json";

    private readonly GeoOpenApiFactory _origem;
    private readonly IServiceScope _escopo;
    private readonly ExecucaoDoFiltroDeVersao _filtro;

    public NegociacaoDaVersaoDeMidiaTests(GeoOpenApiFactory origem)
    {
        ArgumentNullException.ThrowIfNull(origem);

        _origem = origem;
        _escopo = origem.Services.CreateScope();
        _filtro = new ExecucaoDoFiltroDeVersao(_escopo.ServiceProvider, "estado", [1, 2, 3]);
    }

    public void Dispose() => _escopo.Dispose();

    [Theory(DisplayName = "Sem preferência de versão no Accept, a resposta usa a versão mais recente")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("application/json")]
    [InlineData("APPLICATION/JSON")]
    [InlineData("*/*")]
    [InlineData("application/*")]
    public void SemPreferencia_UsaAMaisRecente(string? accept)
    {
        DesfechoDaNegociacao desfecho = _filtro.Negociar(_filtro.NovaRequisicao(accept));

        desfecho.TipoDeConteudo.Should().Be(V3);
    }

    [Theory(DisplayName = "A versão pedida pelo nome vale, em qualquer caixa, e zeros à esquerda são ignorados")]
    [InlineData(V1, V1)]
    [InlineData(V2, V2)]
    [InlineData("Application/VND.Uniplus.ESTADO.V2+JSON", V2)]
    [InlineData("application/vnd.uniplus.estado.v01+json", V1)]
    public void VersaoPeloNome_Vale(string accept, string tipoEsperado)
    {
        DesfechoDaNegociacao desfecho = _filtro.Negociar(_filtro.NovaRequisicao(accept));

        desfecho.TipoDeConteudo.Should().Be(tipoEsperado);
    }

    [Theory(DisplayName = "Vence o tipo de maior q; sem q vale 1, empate fica com o primeiro e tipo desconhecido é ignorado")]
    [InlineData($"{V1};q=0.5, {V2};q=0.9", V2)]
    [InlineData($"{V1}, {V2}", V1)]
    [InlineData($"{V2};q=0.5, {V1}", V1)]
    [InlineData($"{V2};q=0.2, application/json;q=0.8", V3)]
    [InlineData($"{V1};q=0.9, application/json;q=0.5", V1)]
    [InlineData($"text/html, {V2};q=0.5", V2)]
    public void MaiorQ_Vence(string accept, string tipoEsperado)
    {
        DesfechoDaNegociacao desfecho = _filtro.Negociar(_filtro.NovaRequisicao(accept));

        desfecho.TipoDeConteudo.Should().Be(tipoEsperado);
    }

    [Theory(DisplayName = "Tipo com q=0 é excluído; o curinga excluído não barra a versão pedida pelo nome")]
    [InlineData($"application/json, {V2};q=0", V3)]
    [InlineData($"application/json;q=0, {V2}", V2)]
    [InlineData($"*/*;q=0, {V1}", V1)]
    public void Exclusao_PorQZero_AceitaOQueSobra(string accept, string tipoEsperado)
    {
        DesfechoDaNegociacao desfecho = _filtro.Negociar(_filtro.NovaRequisicao(accept));

        desfecho.TipoDeConteudo.Should().Be(tipoEsperado);
    }

    [Theory(DisplayName = "Exclusão por q=0 que deixa nada a aceitar resulta em 406, e a exclusão da versão mais recente não cai para uma anterior")]
    [InlineData($"application/json, {V3};q=0")]
    [InlineData("application/json;q=0")]
    [InlineData("*/*;q=0, application/json")]
    [InlineData("application/*;q=0, application/json")]
    [InlineData(V2 + ";q=0")]
    [InlineData($"{V2};q=0, {V2}")]
    public void Exclusao_PorQZero_SemSobra_Recusa(string accept)
    {
        DesfechoDaNegociacao desfecho = _filtro.Negociar(_filtro.NovaRequisicao(accept));

        desfecho.Recusa!.StatusCode.Should().Be(StatusCodes.Status406NotAcceptable);
    }

    [Theory(DisplayName = "Versão inexistente, outro recurso, tipo estranho ou Accept malformado resultam em 406")]
    [InlineData("application/vnd.uniplus.estado.v9+json")]
    [InlineData("application/vnd.uniplus.cidade.v1+json")]
    [InlineData("application/vnd.uniplus.estado.v1")]
    [InlineData("application/xml")]
    [InlineData("text/html")]
    [InlineData("sem-barra")]
    [InlineData("a/b/c")]
    public void AcceptSemVersaoServida_Recusa(string accept)
    {
        DesfechoDaNegociacao desfecho = _filtro.Negociar(_filtro.NovaRequisicao(accept));

        desfecho.Recusa!.StatusCode.Should().Be(StatusCodes.Status406NotAcceptable);
    }

    [Fact(DisplayName = "A recusa 406 é um problema com tipo, título, detalhe, instância v7 e as extensões code, available_versions e traceId, nessa ordem")]
    public void Recusa_TemOCorpoDoProblema()
    {
        ProblemDetails problema = _filtro.Negociar(_filtro.NovaRequisicao(V2 + ";q=0")).Problema;

        problema.Status.Should().Be(StatusCodes.Status406NotAcceptable);
        problema.Type.Should().EndWith("/uniplus.contract.versao_nao_suportada");
        problema.Title.Should().Be("Versão de mídia não suportada");
        problema.Detail.Should().Be("Nenhuma versão suportada pelo recurso 'estado' foi aceita pelo cliente.");
        Guid.Parse(problema.Instance!["urn:uuid:".Length..]).Version.Should().Be(7);
        problema.Extensions.Keys.Should().Equal("code", "available_versions", "traceId");
        problema.Extensions["code"].Should().Be("uniplus.contract.versao_nao_suportada");
        problema.Extensions["available_versions"].Should().BeEquivalentTo(new[] { 1, 2, 3 });
        problema.Extensions["traceId"].Should().BeOfType<string>().Which.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact(DisplayName = "A recusa 406 declara o tipo de conteúdo application/problem+json")]
    public void Recusa_DeclaraProblemJson()
    {
        DesfechoDaNegociacao desfecho = _filtro.Negociar(_filtro.NovaRequisicao("application/xml"));

        desfecho.Recusa!.ContentTypes.Should().Equal("application/problem+json");
    }

    [Theory(DisplayName = "A resposta aceita declara Vary: Accept uma só vez, sem repetir o que já foi declarado")]
    [InlineData(null, new[] { "Accept" })]
    [InlineData("Accept", new[] { "Accept" })]
    [InlineData("accept-language, ACCEPT", new[] { "accept-language, ACCEPT" })]
    [InlineData("Accept-Encoding", new[] { "Accept-Encoding", "Accept" })]
    [InlineData("Accept-Language", new[] { "Accept-Language", "Accept" })]
    public void Vary_DeclaraAcceptSemRepetir(string? varyExistente, string[] esperado)
    {
        DefaultHttpContext requisicao = _filtro.NovaRequisicao(V2);
        if (varyExistente is not null)
        {
            requisicao.Response.Headers.Vary = varyExistente;
        }

        DesfechoDaNegociacao desfecho = _filtro.Negociar(requisicao);

        desfecho.Vary.Should().Equal(esperado);
    }

    [Theory(DisplayName = "Resposta de erro da ação mantém o tipo de conteúdo e não ganha Vary")]
    [InlineData(true)]
    [InlineData(false)]
    public void RespostaDeErroDaAcao_NaoEReescrita(bool comProblemaNoCorpo)
    {
        ObjectResult erro = comProblemaNoCorpo
            ? new ObjectResult(new ProblemDetails())
            : new ObjectResult("falha") { ContentTypes = { "application/problem+json" } };

        DesfechoDaNegociacao desfecho = _filtro.Negociar(_filtro.NovaRequisicao(V2), erro);

        desfecho.TipoDeConteudo.Should().BeNull();
        desfecho.Vary.Should().BeEmpty();
    }

    [Theory(DisplayName = "Filtro sem recurso ou sem versões configurados lança InvalidOperationException")]
    [InlineData("", new[] { 1 })]
    [InlineData("estado", new int[0])]
    public void Configuracao_Incompleta_Lanca(string recurso, int[] versoes)
    {
        ExecucaoDoFiltroDeVersao incompleto = new(_escopo.ServiceProvider, recurso, versoes);

        Action negociar = () => incompleto.Negociar(incompleto.NovaRequisicao(null));

        negociar.Should().Throw<InvalidOperationException>()
            .WithMessage("VendorMediaTypeAttribute requer Resource e Versions configurados.");
    }

    [Theory(DisplayName = "Na rota de estados, Accept ausente, application/json ou */* dão a versão 1, com Vary: Accept uma só vez")]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("*/*")]
    public async Task RotaDeEstados_SemPreferencia_DaAVersao1(string? accept)
    {
        using HostDasListagens host = HostDasListagens.SemBanco(_origem, new RelogioFixo(), new ConsultaDeListagemCapturada());
        using HttpClient cliente = host.CriarCliente();

        using HttpResponseMessage resposta = await EnviarAsync(cliente, accept);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be(V1);
        resposta.Headers.Vary.Count(valor => string.Equals(valor, "Accept", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact(DisplayName = "Na rota de estados, a versão 2 resulta em 406 problem+json com code, available_versions e traceId, nessa ordem")]
    public async Task RotaDeEstados_Versao2_Recusa406ComProblemJson()
    {
        using HostDasListagens host = HostDasListagens.SemBanco(_origem, new RelogioFixo(), new ConsultaDeListagemCapturada());
        using HttpClient cliente = host.CriarCliente();

        using HttpResponseMessage resposta = await EnviarAsync(cliente, V2);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotAcceptable);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        await ConferirExtensoesDoCorpoAsync(resposta);
    }

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string? accept)
    {
        using HttpRequestMessage requisicao = new(HttpMethod.Get, "/api/estados");
        if (accept is not null)
        {
            requisicao.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        }

        return await cliente.SendAsync(requisicao);
    }

    private static async Task ConferirExtensoesDoCorpoAsync(HttpResponseMessage resposta)
    {
        using JsonDocument corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        string[] nomes = ["code", "available_versions", "traceId"];
        corpo.RootElement.EnumerateObject().Select(propriedade => propriedade.Name)
            .Where(nomes.Contains).Should().Equal(nomes);
        corpo.RootElement.GetProperty("available_versions").EnumerateArray()
            .Select(versao => versao.GetInt32()).Should().Equal(1);
    }
}
