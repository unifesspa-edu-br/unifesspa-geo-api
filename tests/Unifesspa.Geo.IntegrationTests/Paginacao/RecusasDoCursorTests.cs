namespace Unifesspa.Geo.IntegrationTests.Paginacao;

using System.Net;
using System.Net.Http;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Core.Pagination;
using Unifesspa.Geo.IntegrationTests.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Cada recusa do limite, da direção e do cursor nas listagens de estados e de cidades: status,
/// código, texto e a ordem em que as conferências acontecem. A recusa vem antes de qualquer consulta.
/// </summary>
public sealed class RecusasDoCursorTests : IClassFixture<GeoOpenApiFactory>, IDisposable
{
    private const string Invalido = "uniplus.cursor.invalido";
    private const string TextoDoCursorInvalido = "O cursor informado é inválido.";

    private readonly RelogioFixo _relogio = new();
    private readonly ConsultaDeListagemCapturada _consultas = new();
    private readonly HostDasListagens _host;

    public RecusasDoCursorTests(GeoOpenApiFactory origem)
    {
        _host = HostDasListagens.SemBanco(origem, _relogio, _consultas);
    }

    public void Dispose() => _host.Dispose();

    [Theory(DisplayName = "Limite fora do intervalo de 1 a 100 ou que não é número inteiro é recusado com 422")]
    [InlineData("estados", "0")]
    [InlineData("estados", "101")]
    [InlineData("estados", "-1")]
    [InlineData("estados", "abc")]
    [InlineData("estados", "1.5")]
    [InlineData("estados", "99999999999")]
    [InlineData("cidades", "0")]
    [InlineData("cidades", "abc")]
    public async Task Limite_ForaDoIntervaloOuIlegivel_Recusa422(string recurso, string limite)
    {
        await ConferirRecusaAsync(
            $"/api/{recurso}?limit={limite}", 422, "uniplus.cursor.limit_invalido", "O parâmetro 'limit' deve estar entre 1 e 100.");
    }

    [Theory(DisplayName = "Limite nas bordas do intervalo é aceito")]
    [InlineData("estados", 1)]
    [InlineData("estados", 100)]
    [InlineData("cidades", 1)]
    [InlineData("cidades", 100)]
    public async Task Limite_NasBordas_EAceito(string recurso, int limite)
    {
        using HttpClient cliente = _host.CriarCliente();

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri($"/api/{recurso}?limit={limite}", UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        _consultas.PaginaRecebida!.Limit.Should().Be(limite);
    }

    [Theory(DisplayName = "Direção que não é next nem prev é recusada com 422")]
    [InlineData("estados", "sideways")]
    [InlineData("estados", "1")]
    [InlineData("estados", "next%20")]
    [InlineData("cidades", "sideways")]
    public async Task Direcao_Desconhecida_Recusa422(string recurso, string direcao)
    {
        await ConferirRecusaAsync(
            $"/api/{recurso}?direction={direcao}", 422, "uniplus.cursor.direcao_invalida", "O parâmetro 'direction' deve ser 'next' ou 'prev'.");
    }

    [Theory(DisplayName = "Cursor ilegível é recusado com 400")]
    [InlineData("estados", "lixo")]
    [InlineData("estados", "@@@")]
    [InlineData("estados", "YWJj")]
    [InlineData("cidades", "lixo")]
    public async Task Cursor_Ilegivel_Recusa400(string recurso, string cursor)
    {
        await ConferirRecusaAsync($"/api/{recurso}?cursor={cursor}", 400, Invalido, TextoDoCursorInvalido);
    }

    [Theory(DisplayName = "Cursor cuja chave de continuação não é um identificador é recusado com 400")]
    [InlineData("estados")]
    [InlineData("cidades")]
    public async Task Cursor_ComContinuacaoQueNaoEIdentificador_Recusa400(string recurso)
    {
        string cursor = await _host.CursorAsync(_host.CargaValida(recurso, 20, PaginationDirection.Next) with { After = "nao-e-identificador" });

        await ConferirRecusaAsync($"/api/{recurso}?cursor={cursor}", 400, Invalido, TextoDoCursorInvalido);
    }

    [Theory(DisplayName = "Cursor de outro recurso, ou com o nome do recurso em outra caixa, é recusado com 400")]
    [InlineData("estados", "cidades")]
    [InlineData("cidades", "estados")]
    [InlineData("estados", "distritos")]
    [InlineData("estados", "Estados")]
    public async Task Cursor_DeOutroRecurso_Recusa400(string recurso, string recursoDoCursor)
    {
        string cursor = await _host.CursorAsync(_host.CargaValida(recursoDoCursor, 20, PaginationDirection.Next));

        await ConferirRecusaAsync($"/api/{recurso}?cursor={cursor}", 400, Invalido, TextoDoCursorInvalido);
    }

    [Theory(DisplayName = "Cursor emitido para uma direção usado com a outra é recusado com 400")]
    [InlineData("estados", PaginationDirection.Next, "&direction=prev")]
    [InlineData("estados", PaginationDirection.Prev, "")]
    [InlineData("estados", PaginationDirection.Prev, "&direction=next")]
    [InlineData("cidades", PaginationDirection.Next, "&direction=prev")]
    public async Task Cursor_ComDirecaoTrocada_Recusa400(string recurso, PaginationDirection emitidoPara, string consulta)
    {
        string cursor = await _host.CursorAsync(_host.CargaValida(recurso, 20, emitidoPara));

        await ConferirRecusaAsync($"/api/{recurso}?cursor={cursor}{consulta}", 400, Invalido, TextoDoCursorInvalido);
    }

    [Theory(DisplayName = "Cursor de continuação sem chave de ordenação é recusado com 400")]
    [InlineData("estados", null)]
    [InlineData("estados", "")]
    [InlineData("estados", "   ")]
    [InlineData("cidades", null)]
    public async Task Cursor_SemChaveDeOrdenacao_Recusa400(string recurso, string? chave)
    {
        string cursor = await _host.CursorAsync(_host.CargaValida(recurso, 20, PaginationDirection.Next) with { SortKey = chave });

        await ConferirRecusaAsync($"/api/{recurso}?cursor={cursor}", 400, Invalido, TextoDoCursorInvalido);
    }

    [Theory(DisplayName = "Cursor que vence no instante atual ou antes é recusado com 410")]
    [InlineData("estados", 0)]
    [InlineData("estados", -3600)]
    [InlineData("cidades", 0)]
    public async Task Cursor_Vencido_Recusa410(string recurso, int segundosAteOVencimento)
    {
        string cursor = await CursorVencidoAsync(recurso, segundosAteOVencimento);

        await ConferirRecusaAsync($"/api/{recurso}?cursor={cursor}", 410, "uniplus.cursor.expirado", "O cursor informado expirou.");
    }

    [Fact(DisplayName = "Cursor vencido de outro recurso é recusado como expirado: o vencimento é conferido antes do recurso")]
    public async Task Cursor_VencidoDeOutroRecurso_Recusa410()
    {
        string cursor = await CursorVencidoAsync("cidades", 0);

        await ConferirRecusaAsync($"/api/estados?cursor={cursor}", 410, "uniplus.cursor.expirado", "O cursor informado expirou.");
    }

    [Fact(DisplayName = "Cursor que vence um segundo depois do instante atual é aceito")]
    public async Task Cursor_VenceUmSegundoDepois_EAceito()
    {
        string cursor = await CursorVencidoAsync("estados", 1);
        using HttpClient cliente = _host.CriarCliente();

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri($"/api/estados?cursor={cursor}", UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory(DisplayName = "As conferências acontecem na ordem: limite, direção e só então o cursor")]
    [InlineData("estados", "limit=0&direction=x&cursor=lixo", "uniplus.cursor.limit_invalido")]
    [InlineData("estados", "limit=0&cursor=lixo", "uniplus.cursor.limit_invalido")]
    [InlineData("estados", "direction=x&cursor=lixo", "uniplus.cursor.direcao_invalida")]
    [InlineData("cidades", "limit=0&direction=x", "uniplus.cursor.limit_invalido")]
    public async Task Conferencias_RespeitamAOrdem(string recurso, string consulta, string codigo)
    {
        using HttpClient cliente = _host.CriarCliente();

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri($"/api/{recurso}?{consulta}", UriKind.Relative));

        (await LerCodigoAsync(resposta)).Should().Be(codigo);
    }

    [Fact(DisplayName = "Limite inválido é recusado com 422 antes de o cursor vencido ser conferido")]
    public async Task Limite_Invalido_VemAntesDoCursorVencido()
    {
        string cursor = await CursorVencidoAsync("estados", 0);

        await ConferirRecusaAsync(
            $"/api/estados?limit=0&cursor={cursor}", 422, "uniplus.cursor.limit_invalido", "O parâmetro 'limit' deve estar entre 1 e 100.");
    }

    private async Task<string> CursorVencidoAsync(string recursoDoCursor, int segundosAteOVencimento)
    {
        CursorPayload carga = _host.CargaValida(recursoDoCursor, 20, PaginationDirection.Next);
        return await _host.CursorAsync(carga with { ExpiresAt = _relogio.GetUtcNow().AddSeconds(segundosAteOVencimento) });
    }

    private async Task ConferirRecusaAsync(string rota, int status, string codigo, string detalhe)
    {
        using HttpClient cliente = _host.CriarCliente();

        using HttpResponseMessage resposta = await cliente.GetAsync(new Uri(rota, UriKind.Relative));

        ((int)resposta.StatusCode).Should().Be(status);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using JsonDocument corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        corpo.RootElement.GetProperty("code").GetString().Should().Be(codigo);
        corpo.RootElement.GetProperty("detail").GetString().Should().Be(detalhe);
        _consultas.PaginaRecebida.Should().BeNull("a recusa acontece antes de qualquer consulta");
    }

    private static async Task<string?> LerCodigoAsync(HttpResponseMessage resposta)
    {
        using JsonDocument corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return corpo.RootElement.GetProperty("code").GetString();
    }
}
