namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>O diagnóstico de uma leitura recusada traz a tabela e a linha, nunca o conteúdo.</summary>
public sealed class DiagnosticoDaLeituraDoPacoteTests
{
    [Fact(DisplayName = "Recusa do pacote registra tipo, tabela e linha sem a mensagem da exceção")]
    public void Recusa_RegistraTabelaELinha()
    {
        ErroDeLeituraDoPacoteDneException recusa = new("logradouro", 42, MotivosDeRecusaDoArquivoSqlDne.ValoresForaDaGramatica);

        string diagnostico = DiagnosticoDeExcecaoSanitizado.Descrever(recusa);

        diagnostico.Should().Contain("TabelaDoPacote=logradouro").And.Contain("Linha=42");
        diagnostico.Should().NotContain(recusa.Message);
    }

    [Fact(DisplayName = "Outra exceção não ganha tabela nem linha")]
    public void OutraExcecao_NaoTemTabelaDoPacote() =>
        DiagnosticoDeExcecaoSanitizado.Descrever(new InvalidOperationException("x")).Should().NotContain("TabelaDoPacote");
}
