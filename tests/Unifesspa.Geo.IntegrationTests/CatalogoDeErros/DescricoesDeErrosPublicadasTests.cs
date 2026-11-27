namespace Unifesspa.Geo.IntegrationTests.CatalogoDeErros;

using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.TestSupport;
using Unifesspa.Geo.IntegrationTests.TestSupport.CatalogoDeErros;

/// <summary>
/// As descrições públicas em <c>contracts/descricoes-de-erros.json</c> cobrem os códigos
/// <c>uniplus.geo.*</c> de <c>contracts/codigos-de-erro.json</c>, nos dois sentidos, e seguem
/// o formato canônico. O Markdown e os links são conferidos pelo portal.
/// </summary>
public sealed class DescricoesDeErrosPublicadasTests
{
    private const string ArquivoDasDescricoes = "contracts/descricoes-de-erros.json";
    private const string ArquivoDosCodigos = "contracts/codigos-de-erro.json";

    [Fact(DisplayName = "As descrições públicas cobrem exatamente os códigos uniplus.geo.* publicados e seguem as regras do texto")]
    public void DescricoesCobremOsCodigosDoGeo()
    {
        DocumentoDeDescricoesDeErros descricoes = DocumentoDeDescricoesDeErros.Ler(LerArquivo(ArquivoDasDescricoes));
        IEnumerable<string> codigos = DocumentoDoCatalogoDeErros.Ler(LerArquivo(ArquivoDosCodigos)).Codigos.Select(static e => e.Codigo);

        string[] problemas = ConferenciaDasDescricoesDeErros.Conferir(descricoes, codigos);

        if (problemas.Length > 0)
        {
            Assert.Fail($"{problemas.Length} problema(s) nas descrições:\n{string.Join('\n', problemas)}");
        }
    }

    [Fact(DisplayName = "O arquivo de descrições está na ordem e na formatação canônicas")]
    public void ArquivoDeDescricoesEstaEmFormatoCanonico()
    {
        string publicado = LerArquivo(ArquivoDasDescricoes);
        string canonico = DocumentoDeDescricoesDeErros.Ler(publicado).SerializarEmOrdem();

        if (!string.Equals(publicado, canonico, StringComparison.Ordinal))
        {
            string caminho = CaminhoNoRepositorio.Resolver(ArquivoDasDescricoes);
            string resultado = ResultadoDeConferencia.Gravar(Path.GetFileName(caminho), canonico);
            Assert.Fail($"As descrições não estão ordenadas por código nem na formatação padrão.\n{ResultadoDeConferencia.Mensagem(resultado, caminho)}");
        }
    }

    private static string LerArquivo(string relativo)
    {
        string caminho = CaminhoNoRepositorio.Resolver(relativo);
        File.Exists(caminho).Should().BeTrue($"{relativo} precisa estar versionado");
        return File.ReadAllText(caminho).ReplaceLineEndings("\n");
    }
}
