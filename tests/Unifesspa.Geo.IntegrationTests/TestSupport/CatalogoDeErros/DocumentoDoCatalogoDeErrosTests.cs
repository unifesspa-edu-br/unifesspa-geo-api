namespace Unifesspa.Geo.IntegrationTests.TestSupport.CatalogoDeErros;

using System.Text.Json;

using AwesomeAssertions;

public sealed class DocumentoDoCatalogoDeErrosTests
{
    private static EntradaDoCatalogoDeErros Entrada(string codigo, int status = 400, string titulo = "Título") =>
        new(codigo, status, titulo);

    [Fact(DisplayName = "Gerar ordena os códigos em ordem ordinal e usa o formato 1")]
    public void Gerar_OrdenaPorCodigo()
    {
        DocumentoDoCatalogoDeErros documento = DocumentoDoCatalogoDeErros.Gerar([Entrada("uniplus.b"), Entrada("uniplus.B"), Entrada("uniplus.a")]);

        documento.VersaoDoFormato.Should().Be(1);
        documento.Codigos.Select(e => e.Codigo).Should().Equal("uniplus.B", "uniplus.a", "uniplus.b");
    }

    [Fact(DisplayName = "Gerar une o mesmo código declarado duas vezes com o mesmo status e título")]
    public void Gerar_UneDeclaracoesIdenticas()
    {
        DocumentoDoCatalogoDeErros documento = DocumentoDoCatalogoDeErros.Gerar([Entrada("uniplus.a"), Entrada("uniplus.a")]);

        documento.Codigos.Should().ContainSingle();
    }

    [Fact(DisplayName = "Gerar recusa o mesmo código com título diferente e cita o código e os dois títulos")]
    public void Gerar_RecusaConflito()
    {
        Action gerar = () => DocumentoDoCatalogoDeErros.Gerar([Entrada("uniplus.a", 400, "Um"), Entrada("uniplus.a", 400, "Dois")]);

        gerar.Should().Throw<InvalidOperationException>().WithMessage("*uniplus.a*\"Um\"*\"Dois\"*");
    }

    [Fact(DisplayName = "Serializar e ler devolvem o mesmo documento, com acentos legíveis e fim de linha")]
    public void Serializar_RoundTripEstavel()
    {
        DocumentoDoCatalogoDeErros original = DocumentoDoCatalogoDeErros.Gerar([Entrada("uniplus.a", 409, "Já existe uma importação")]);

        string json = original.Serializar();

        json.Should().EndWith("}\n").And.Contain("Já existe").And.Contain("\"versaoDoFormato\": 1");
        DocumentoDoCatalogoDeErros.Ler(json).Serializar().Should().Be(json);
    }

    [Fact(DisplayName = "Ler lança exceção para JSON inválido")]
    public void Ler_JsonInvalidoLanca()
    {
        Action ler = () => DocumentoDoCatalogoDeErros.Ler("{ não é json");

        ler.Should().Throw<JsonException>();
    }
}
