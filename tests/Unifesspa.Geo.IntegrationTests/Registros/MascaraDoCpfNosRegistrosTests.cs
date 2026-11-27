namespace Unifesspa.Geo.IntegrationTests.Registros;

using AwesomeAssertions;

using NSubstitute;

using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

using Unifesspa.Geo.Infrastructure.Core.Logging;
using Unifesspa.Geo.IntegrationTests.TestSupport;

/// <summary>
/// O CPF que aparece em qualquer valor de um registro (texto simples, estrutura, sequência, chave ou valor de
/// dicionário) sai como <c>***.ddd.ddd-**</c>; o que não contém CPF segue a mesma instância.
/// </summary>
public sealed class MascaraDoCpfNosRegistrosTests
{
    private const string Propriedade = "Dado";

    private static readonly CpfFicticio Primeiro = CpfFicticio.Gerar(531246897);
    private static readonly CpfFicticio Segundo = CpfFicticio.Gerar(274915386);

    [Fact(DisplayName = "CPF com pontuação dentro de um texto sai mascarado")]
    public void CpfComPontuacaoDentroDoTextoSaiMascarado()
    {
        LogEventPropertyValue resultado = Mascarar(new ScalarValue($"CPF {Primeiro.Pontuado} informado"));

        Texto(resultado).Should().Be($"CPF {Primeiro.Mascarado} informado");
    }

    [Fact(DisplayName = "CPF de 11 dígitos sem pontuação sai no formato ***.ddd.ddd-**")]
    public void CpfSemPontuacaoSaiNoFormatoMascarado()
    {
        LogEventPropertyValue resultado = Mascarar(new ScalarValue(Primeiro.Digitos));

        Texto(resultado).Should().Be(Primeiro.Mascarado);
        Texto(resultado).Should().MatchRegex(@"^\*\*\*\.\d{3}\.\d{3}-\*\*$");
    }

    [Fact(DisplayName = "CPF com pontuação parcial também é mascarado")]
    public void CpfComPontuacaoParcialTambemEMascarado()
    {
        string parcial = $"{Primeiro.Digitos[..3]}.{Primeiro.Digitos[3..9]}-{Primeiro.Digitos[9..]}";

        Texto(Mascarar(new ScalarValue(parcial))).Should().Be(Primeiro.Mascarado);
    }

    [Fact(DisplayName = "Dois CPFs no mesmo texto são mascarados cada um")]
    public void DoisCpfsNoMesmoTextoSaoMascarados()
    {
        LogEventPropertyValue resultado = Mascarar(new ScalarValue($"{Primeiro.Pontuado} e {Segundo.Digitos}"));

        Texto(resultado).Should().Be($"{Primeiro.Mascarado} e {Segundo.Mascarado}");
    }

    [Theory(DisplayName = "Número maior que um CPF, com mais ou menos dígitos, não é mascarado")]
    [InlineData("1234567890123")]
    [InlineData("id 123456789012345 fim")]
    [InlineData("1234567890")]
    [InlineData("9123.456.789-09")]
    public void NumeroMaiorOuMenorQueUmCpfNaoEMascarado(string texto)
    {
        ScalarValue original = new(texto);

        Mascarar(original).Should().BeSameAs(original);
    }

    [Fact(DisplayName = "Propriedade sem CPF mantém a mesma instância")]
    public void PropriedadeSemCpfMantemAMesmaInstancia()
    {
        ScalarValue texto = new("Marabá, Pará");
        ScalarValue numero = new(12345678909L);

        Mascarar(texto).Should().BeSameAs(texto);
        Mascarar(numero).Should().BeSameAs(numero);
    }

    [Fact(DisplayName = "Texto vazio ou nulo volta como veio")]
    public void TextoVazioOuNuloVoltaComoVeio()
    {
        PiiMaskingEnricher.MascararCpf(string.Empty).Should().BeSameAs(string.Empty);
        Mascarar(new ScalarValue(null)).Should().BeOfType<ScalarValue>().Which.Value.Should().BeNull();
    }

    [Fact(DisplayName = "Estrutura mascara só a propriedade com CPF e mantém a etiqueta e as demais instâncias")]
    public void EstruturaMascaraSoAPropriedadeComCpf()
    {
        LogEventProperty nome = new("Nome", new ScalarValue("Maria Teste"));
        LogEventProperty documento = new("Documento", new ScalarValue(Primeiro.Pontuado));

        StructureValue resultado = (StructureValue)Mascarar(new StructureValue([nome, documento], "Candidato"));

        resultado.TypeTag.Should().Be("Candidato");
        resultado.Properties[0].Should().BeSameAs(nome);
        Texto(resultado.Properties[1].Value).Should().Be(Primeiro.Mascarado);
    }

    [Fact(DisplayName = "Estrutura sem CPF mantém a mesma instância")]
    public void EstruturaSemCpfMantemAMesmaInstancia()
    {
        StructureValue original = new([new LogEventProperty("Nome", new ScalarValue("Maria Teste"))]);

        Mascarar(original).Should().BeSameAs(original);
    }

    [Fact(DisplayName = "Estrutura dentro de estrutura dentro de sequência é mascarada em todos os níveis")]
    public void EstruturaAninhadaEMascaradaEmTodosOsNiveis()
    {
        StructureValue interna = new([new LogEventProperty("Cpf", new ScalarValue(Primeiro.Digitos))]);
        StructureValue externa = new([new LogEventProperty("Titular", interna)]);

        SequenceValue resultado = (SequenceValue)Mascarar(new SequenceValue([new ScalarValue("a"), externa]));

        StructureValue titular = (StructureValue)((StructureValue)resultado.Elements[1]).Properties[0].Value;
        Texto(titular.Properties[0].Value).Should().Be(Primeiro.Mascarado);
    }

    [Fact(DisplayName = "Sequência mascara só o elemento com CPF e mantém os outros")]
    public void SequenciaMascaraSoOElementoComCpf()
    {
        ScalarValue antes = new("antes");
        ScalarValue depois = new(7);

        SequenceValue resultado = (SequenceValue)Mascarar(new SequenceValue([antes, new ScalarValue(Primeiro.Pontuado), depois]));

        resultado.Elements[0].Should().BeSameAs(antes);
        Texto(resultado.Elements[1]).Should().Be(Primeiro.Mascarado);
        resultado.Elements[2].Should().BeSameAs(depois);
    }

    [Fact(DisplayName = "Sequência sem CPF mantém a mesma instância")]
    public void SequenciaSemCpfMantemAMesmaInstancia()
    {
        SequenceValue original = new([new ScalarValue("a"), new ScalarValue(1)]);

        Mascarar(original).Should().BeSameAs(original);
    }

    [Fact(DisplayName = "Dicionário mascara a chave e o valor e mantém as entradas sem CPF")]
    public void DicionarioMascaraChaveEValor()
    {
        ScalarValue chaveSimples = new("codigo");
        ScalarValue valorSimples = new("ok");
        DictionaryValue original = new(
        [
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(chaveSimples, valorSimples),
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue(Primeiro.Pontuado), new ScalarValue(Segundo.Digitos)),
        ]);

        DictionaryValue resultado = (DictionaryValue)Mascarar(original);

        resultado.Elements[chaveSimples].Should().BeSameAs(valorSimples);
        Texto(resultado.Elements[new ScalarValue(Primeiro.Mascarado)]).Should().Be(Segundo.Mascarado);
        resultado.Elements.Should().HaveCount(2);
    }

    [Fact(DisplayName = "Dicionário com chave que não é texto e valor com CPF mascara o valor")]
    public void DicionarioComChaveNumericaMascaraOValor()
    {
        ScalarValue chave = new(10);
        DictionaryValue original = new([new KeyValuePair<ScalarValue, LogEventPropertyValue>(chave, new ScalarValue(Primeiro.Pontuado))]);

        DictionaryValue resultado = (DictionaryValue)Mascarar(original);

        Texto(resultado.Elements[chave]).Should().Be(Primeiro.Mascarado);
    }

    [Fact(DisplayName = "Dicionário sem CPF mantém a mesma instância")]
    public void DicionarioSemCpfMantemAMesmaInstancia()
    {
        DictionaryValue original = new([new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("a"), new ScalarValue("b"))]);

        Mascarar(original).Should().BeSameAs(original);
    }

    [Fact(DisplayName = "O enriquecimento troca só a propriedade com CPF e deixa as demais intactas")]
    public void EnriquecimentoTrocaSoAPropriedadeComCpf()
    {
        ScalarValue limpa = new("sem dado pessoal");
        LogEvent evento = NovoEvento(
            new LogEventProperty("Limpa", limpa),
            new LogEventProperty("Suja", new ScalarValue(Primeiro.Pontuado)));

        new PiiMaskingEnricher().Enrich(evento, Substitute.For<ILogEventPropertyFactory>());

        evento.Properties["Limpa"].Should().BeSameAs(limpa);
        Texto(evento.Properties["Suja"]).Should().Be(Primeiro.Mascarado);
    }

    private static LogEventPropertyValue Mascarar(LogEventPropertyValue valor)
    {
        LogEvent evento = NovoEvento(new LogEventProperty(Propriedade, valor));

        new PiiMaskingEnricher().Enrich(evento, Substitute.For<ILogEventPropertyFactory>());

        return evento.Properties[Propriedade];
    }

    private static LogEvent NovoEvento(params LogEventProperty[] propriedades) =>
        new(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("mensagem"),
            propriedades);

    private static string? Texto(LogEventPropertyValue valor) => ((ScalarValue)valor).Value as string;
}
