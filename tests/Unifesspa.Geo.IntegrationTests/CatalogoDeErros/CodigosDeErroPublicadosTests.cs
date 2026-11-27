namespace Unifesspa.Geo.IntegrationTests.CatalogoDeErros;

using System.Text.Json;
using System.Text.RegularExpressions;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Infrastructure.Core.Errors;
using Unifesspa.Geo.Infrastructure.Core.OpenApi;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.IntegrationTests.TestSupport;
using Unifesspa.Geo.IntegrationTests.TestSupport.CatalogoDeErros;

/// <summary>
/// O arquivo <c>contracts/codigos-de-erro.json</c> lista os códigos de erro de fio que o Geo emite,
/// gerados dos registros de erro do host. Código novo, removido, ou status e título alterados
/// reprovam até o arquivo ser aprovado.
/// </summary>
public sealed class CodigosDeErroPublicadosTests : IClassFixture<GeoOpenApiFactory>
{
    private const string ArquivoPublicado = "contracts/codigos-de-erro.json";
    private const string PrefixoDoGeo = "uniplus.geo.";

    private readonly GeoOpenApiFactory _factory;

    public CodigosDeErroPublicadosTests(GeoOpenApiFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "O arquivo de códigos de erro bate com os códigos que o Geo emite")]
    public void ArquivoPublicadoBateComOsCodigosEmitidos()
    {
        string caminho = CaminhoNoRepositorio.Resolver(ArquivoPublicado);
        string gerado = GerarCatalogo().Serializar();
        string? publicado = File.Exists(caminho) ? File.ReadAllText(caminho).ReplaceLineEndings("\n") : null;

        if (!string.Equals(publicado, gerado, StringComparison.Ordinal))
        {
            string resultado = ResultadoDeConferencia.Gravar(Path.GetFileName(caminho), gerado);
            Assert.Fail($"{string.Join('\n', CompararPublicado(publicado))}\n{ResultadoDeConferencia.Mensagem(resultado, caminho)}");
        }
    }

    [Fact(DisplayName = "Todo código de erro do Geo começa com o prefixo uniplus.")]
    public void TodoCodigoComecaComOPrefixoDaPlataforma()
    {
        string[] foraDoPadrao =
        [
            .. Codigos().Where(static c => !c.StartsWith("uniplus.", StringComparison.Ordinal)),
        ];

        foraDoPadrao.Should().BeEmpty("o código de fio é uniplus.<módulo>.<assunto>.<motivo>");
    }

    [Fact(DisplayName = "Todo código de erro do Geo casa com o padrão publicado no contrato OpenAPI")]
    public void TodoCodigoCasaComOPadraoDoContrato()
    {
        var padrao = new Regex(ProblemDetailsExtensionsDocumentTransformer.PadraoDoCodigo, RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

        Codigos().Where(c => !padrao.IsMatch(c)).Should().BeEmpty();
    }

    [Fact(DisplayName = "Código do módulo Geo tem a forma uniplus.geo.<assunto>.<motivo>")]
    public void CodigoDoGeoTemAssuntoEMotivo()
    {
        var forma = new Regex(@"^uniplus\.geo\.[a-z0-9]+(_[a-z0-9]+)*\.[a-z0-9]+(_[a-z0-9]+)*$", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

        Codigos().Where(c => c.StartsWith(PrefixoDoGeo, StringComparison.Ordinal) && !forma.IsMatch(c)).Should().BeEmpty();
    }

    [Fact(DisplayName = "O mesmo código com status ou título diferentes recusa a geração e cita os dois")]
    public void GerarRecusaCodigoComStatusOuTituloDiferentes()
    {
        Action gerar = () => DocumentoDoCatalogoDeErros.Gerar(
        [
            new EntradaDoCatalogoDeErros("uniplus.geo.x.y", 409, "Um"),
            new EntradaDoCatalogoDeErros("uniplus.geo.x.y", 422, "Um"),
        ]);

        gerar.Should().Throw<InvalidOperationException>()
            .WithMessage("*uniplus.geo.x.y*409 \"Um\"*422 \"Um\"*");
    }

    private IEnumerable<string> Codigos() => GerarCatalogo().Codigos.Select(static e => e.Codigo);

    private DocumentoDoCatalogoDeErros GerarCatalogo() =>
        DocumentoDoCatalogoDeErros.Gerar(
            _factory.Services.GetServices<IDomainErrorRegistration>()
                .SelectMany(static r => r.GetMappings())
                .Select(static par => new EntradaDoCatalogoDeErros(par.Value.Code, par.Value.Status, par.Value.Title)));

    private IReadOnlyList<string> CompararPublicado(string? publicado)
    {
        if (publicado is null)
        {
            return ["O catálogo publicado está ausente."];
        }

        try
        {
            return DivergenciasDoCatalogoDeErros.Comparar(DocumentoDoCatalogoDeErros.Ler(publicado).Codigos, GerarCatalogo().Codigos);
        }
        catch (JsonException)
        {
            return ["O catálogo publicado contém JSON inválido ou campos fora do formato."];
        }
    }
}
