namespace Unifesspa.Geo.IntegrationTests.TestSupport;

using AwesomeAssertions;

public sealed class ResultadoDeConferenciaTests
{
    [Fact(DisplayName = "Grava o resultado obtido numa pasta temporária com o nome do arquivo versionado")]
    public void Gravar_GravaNaPastaTemporariaComONomeInformado()
    {
        string caminho = ResultadoDeConferencia.Gravar("contrato-teste-a.json", "{\"a\":1}");

        Path.GetFileName(caminho).Should().Be("contrato-teste-a.json");
        caminho.Should().StartWith(Path.GetTempPath());
        File.ReadAllText(caminho).Should().Be("{\"a\":1}");
    }

    [Fact(DisplayName = "Duas gravações do mesmo nome não colidem: cada uma tem a própria pasta")]
    public void Gravar_DuasGravacoesNaoColidem()
    {
        string primeiro = ResultadoDeConferencia.Gravar("contrato-teste-b.json", "primeiro");
        string segundo = ResultadoDeConferencia.Gravar("contrato-teste-b.json", "segundo");

        segundo.Should().NotBe(primeiro);
        File.ReadAllText(primeiro).Should().Be("primeiro");
        File.ReadAllText(segundo).Should().Be("segundo");
    }

    [Fact(DisplayName = "Ignora diretórios no nome informado para não gravar fora da pasta temporária")]
    public void Gravar_IgnoraDiretoriosNoNome()
    {
        string caminho = ResultadoDeConferencia.Gravar("../../contrato-teste-c.json", "x");

        caminho.Should().StartWith(Path.GetTempPath());
        Path.GetFileName(caminho).Should().Be("contrato-teste-c.json");
    }

    [Fact(DisplayName = "A mensagem cita o resultado obtido e o comando cp com origem e destino")]
    public void Mensagem_CitaOrigemEDestinoNoComandoDeAprovacao()
    {
        string mensagem = ResultadoDeConferencia.Mensagem("/tmp/x/obtido.json", "contracts/obtido.json");

        mensagem.Should().Contain("resultado obtido: /tmp/x/obtido.json");
        mensagem.Should().Contain("cp \"/tmp/x/obtido.json\" \"contracts/obtido.json\"");
        mensagem.Should().Contain("git diff --no-index \"contracts/obtido.json\" \"/tmp/x/obtido.json\"");
    }

    [Fact(DisplayName = "A mensagem escapa caracteres que o shell interpretaria dentro de aspas")]
    public void Mensagem_EscapaCaracteresEspeciais()
    {
        string mensagem = ResultadoDeConferencia.Mensagem("/tmp/a$b`c", "contracts/x.json");

        mensagem.Should().Contain("cp \"/tmp/a\\$b\\`c\"").And.Contain("--no-index \"contracts/x.json\" \"/tmp/a\\$b\\`c\"");
    }
}
