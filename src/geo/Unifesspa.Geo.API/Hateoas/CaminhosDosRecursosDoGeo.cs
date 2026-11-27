namespace Unifesspa.Geo.API.Hateoas;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Unifesspa.Geo.API.Controllers;

/// <summary>
/// Conhece como chegar a cada recurso do Geo: ação, controller e nome do valor de rota.
/// Resolve URIs relativas (sem scheme/host) via <see cref="LinkGenerator"/>, para os
/// <c>_links</c> hypermedia (HATEOAS Level 1, ADR-0029). Os montadores de links só dizem
/// para qual recurso apontam.
/// </summary>
internal sealed class CaminhosDosRecursosDoGeo
{
    private const string SufixoDeController = "Controller";

    private static readonly string Cidades = SemSufixoDeController(nameof(CidadesController));
    private static readonly string Estados = SemSufixoDeController(nameof(EstadosController));
    private static readonly string HierarquiaDaCidade = SemSufixoDeController(nameof(CidadeHierarquiaController));
    private static readonly string ConsultaDoCep = SemSufixoDeController(nameof(CepController));
    private static readonly string Importacoes = SemSufixoDeController(nameof(GeoImportacoesController));

    private readonly LinkGenerator _linkGenerator;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CaminhosDosRecursosDoGeo(LinkGenerator linkGenerator, IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(linkGenerator);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _linkGenerator = linkGenerator;
        _httpContextAccessor = httpContextAccessor;
    }

    public string DaCidade(string codigoIbge) =>
        Resolver(nameof(CidadesController.ObterPorCodigoIbge), Cidades, new { codigoIbge });

    public string DaListaDeCidades() =>
        Resolver(nameof(CidadesController.Listar), Cidades, valores: null);

    /// <summary>A UF é parâmetro de consulta, não de rota: o <see cref="LinkGenerator"/> não a inclui nos valores.</summary>
    public string DaListaDeCidadesDaUf(string uf) =>
        $"{DaListaDeCidades()}?uf={Uri.EscapeDataString(uf)}";

    public string DoEstado(string uf) =>
        Resolver(nameof(EstadosController.ObterPorUf), Estados, new { uf });

    public string DaListaDeEstados() =>
        Resolver(nameof(EstadosController.Listar), Estados, valores: null);

    public string DosDistritosDaCidade(string codigoIbge) =>
        Resolver(nameof(CidadeHierarquiaController.ListarDistritos), HierarquiaDaCidade, new { codigoIbge });

    public string DosBairrosDaCidade(string codigoIbge) =>
        Resolver(nameof(CidadeHierarquiaController.ListarBairros), HierarquiaDaCidade, new { codigoIbge });

    public string DosLogradourosDaCidade(string codigoIbge) =>
        Resolver(nameof(CidadeHierarquiaController.ListarLogradouros), HierarquiaDaCidade, new { codigoIbge });

    public string DaConsultaDoCep(string cep) =>
        Resolver(nameof(CepController.Resolver), ConsultaDoCep, new { cep });

    public string DaImportacao(Guid id) =>
        Resolver(nameof(GeoImportacoesController.Obter), Importacoes, new { id });

    private static string SemSufixoDeController(string nome) =>
        nome.EndsWith(SufixoDeController, StringComparison.Ordinal)
            ? nome[..^SufixoDeController.Length]
            : nome;

    /// <summary>
    /// Resolve o path da ação; lança se a rota não for resolvível (config de rota
    /// inconsistente — falha cedo em vez de emitir link quebrado).
    /// </summary>
    private string Resolver(string acao, string controller, object? valores)
    {
        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        string? caminho = httpContext is not null
            ? _linkGenerator.GetPathByAction(httpContext, action: acao, controller: controller, values: valores)
            : _linkGenerator.GetPathByAction(action: acao, controller: controller, values: valores);

        return caminho
            ?? throw new InvalidOperationException(
                $"LinkGenerator não conseguiu resolver a rota para {controller}.{acao}. " +
                "Verifique o registro do controller e o template de rota.");
    }
}
