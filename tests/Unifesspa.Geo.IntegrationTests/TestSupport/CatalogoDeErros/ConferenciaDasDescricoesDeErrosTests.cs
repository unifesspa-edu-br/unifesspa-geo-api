namespace Unifesspa.Geo.IntegrationTests.TestSupport.CatalogoDeErros;

using AwesomeAssertions;

public sealed class ConferenciaDasDescricoesDeErrosTests
{
    private const string Codigo = "uniplus.geo.cep.nao_encontrado";

    private static DescricaoDeErro Valida(string codigo = Codigo) =>
        new(codigo, ["UNI-REQ-0530"], ["Aconteceu."], ["Resolva."]);

    private static string[] Conferir(DescricaoDeErro?[] descricoes, params string[] publicados) =>
        ConferenciaDasDescricoesDeErros.Conferir(new DocumentoDeDescricoesDeErros(1, descricoes), publicados);

    [Fact(DisplayName = "Descrição completa de código do Geo não gera problema")]
    public void DescricaoValida_NaoGeraProblema() => Conferir([Valida()], Codigo).Should().BeEmpty();

    [Fact(DisplayName = "Código do Geo sem descrição é apontado")]
    public void CodigoDoGeoSemDescricao() => Conferir([], Codigo).Should().Equal($"{Codigo}: código sem descrição.");

    [Fact(DisplayName = "Descrição de código do Geo que o catálogo não tem é apontada")]
    public void DescricaoSemCodigo() => Conferir([Valida()]).Should().Equal($"{Codigo}: descrição sem código.");

    [Fact(DisplayName = "Código comum do catálogo não precisa de descrição, e descrevê-lo é erro")]
    public void CodigoComumEDescritoPelaApi()
    {
        Conferir([], "uniplus.validacao").Should().BeEmpty();
        Conferir([Valida("uniplus.validacao")], "uniplus.validacao").Should().ContainSingle().Which.Should().Contain("descritos pela API");
    }

    [Fact(DisplayName = "Descrição repetida é apontada")]
    public void DescricaoRepetida() =>
        Conferir([Valida(), Valida()], Codigo).Should().Contain($"{Codigo}: descrição repetida.");

    [Fact(DisplayName = "Versão do formato desconhecida é apontada")]
    public void VersaoDesconhecida() =>
        ConferenciaDasDescricoesDeErros.Conferir(new DocumentoDeDescricoesDeErros(2, [Valida()]), [Codigo])
            .Should().Contain("descricoes-de-erros.json: versão do formato desconhecida.");

    [Fact(DisplayName = "Seção obrigatória vazia ou bloco vazio é apontado")]
    public void SecaoObrigatoriaVazia()
    {
        Conferir([Valida() with { OQueAconteceu = [] }], Codigo).Should().ContainSingle().Which.Should().Contain("oQueAconteceu");
        Conferir([Valida() with { ComoResolver = [" "] }], Codigo).Should().ContainSingle().Which.Should().Contain("bloco vazio");
    }

    [Fact(DisplayName = "Seção ondeAparece é opcional, mas não pode ter bloco vazio")]
    public void OndeApareceOpcional()
    {
        Conferir([Valida() with { OndeAparece = null }], Codigo).Should().BeEmpty();
        Conferir([Valida() with { OndeAparece = [""] }], Codigo).Should().ContainSingle().Which.Should().Contain("ondeAparece");
    }

    [Fact(DisplayName = "Requisitos são obrigatórios, podem ser vazios e usam UNI-REQ-NNNN")]
    public void Requisitos()
    {
        Conferir([Valida() with { Requisitos = [] }], Codigo).Should().BeEmpty();
        Conferir([Valida() with { Requisitos = null }], Codigo).Should().ContainSingle();
        Conferir([Valida() with { Requisitos = ["RN12"] }], Codigo).Should().ContainSingle().Which.Should().Contain("UNI-REQ-NNNN");
    }

    [Fact(DisplayName = "Entrada sem código ou com código fora da forma é apontada")]
    public void CodigoForaDaForma()
    {
        Conferir([null], Codigo).Should().Contain("descricoes-de-erros.json: entrada com código ausente ou inválido.");
        Conferir([Valida("Erro.Qualquer")]).Should().Contain("descricoes-de-erros.json: entrada com código ausente ou inválido.");
    }

    [Fact(DisplayName = "Dado pessoal no texto é apontado")]
    public void DadoPessoalNoTexto() =>
        Conferir([Valida() with { OQueAconteceu = ["Fale com a@b.com"] }], Codigo).Should().ContainSingle().Which.Should().Contain("e-mail");
}
