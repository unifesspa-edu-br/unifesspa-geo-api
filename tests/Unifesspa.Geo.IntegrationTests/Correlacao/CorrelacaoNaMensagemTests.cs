namespace Unifesspa.Geo.IntegrationTests.Correlacao;

using System.Diagnostics;

using AwesomeAssertions;

using NSubstitute;

using Unifesspa.Geo.Infrastructure.Core.Messaging.Middleware;
using Unifesspa.Geo.Infrastructure.Core.Middleware;

using Wolverine;

/// <summary>
/// O identificador de correlação da mensagem: o cabeçalho válido vale mais que o valor do ambiente da requisição,
/// que vale mais que um GUID novo; o cabeçalho é reescrito com o valor final e o escopo de log termina no fim.
/// </summary>
public sealed class CorrelacaoNaMensagemTests
{
    private const string Cabecalho = "uniplus.correlation-id";

    [Fact(DisplayName = "Cabeçalho válido da mensagem vale mais que o valor do ambiente")]
    public void CabecalhoValidoVenceOAmbiente()
    {
        Envelope envelope = ComCabecalho("id-da-mensagem");

        using IDisposable escopo = CorrelationIdEnvelopeMiddleware.Before(envelope, Ambiente("id-do-ambiente"));

        envelope.Headers[Cabecalho].Should().Be("id-da-mensagem");
    }

    [Theory(DisplayName = "Cabeçalho ausente, vazio ou inválido usa o valor do ambiente")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("com espaco")]
    public void CabecalhoAusenteOuInvalidoUsaOAmbiente(string? cabecalho)
    {
        Envelope envelope = ComCabecalho(cabecalho);

        using IDisposable escopo = CorrelationIdEnvelopeMiddleware.Before(envelope, Ambiente("id-do-ambiente"));

        envelope.Headers[Cabecalho].Should().Be("id-do-ambiente");
    }

    [Theory(DisplayName = "Sem cabeçalho válido nem ambiente válido gera um GUID novo e reescreve o cabeçalho")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("com espaco")]
    public void SemNadaValidoGeraGuid(string? ambiente)
    {
        Envelope envelope = ComCabecalho("também inválido");

        using IDisposable escopo = CorrelationIdEnvelopeMiddleware.Before(envelope, Ambiente(ambiente));

        Guid.TryParseExact(envelope.Headers[Cabecalho], "D", out _).Should().BeTrue();
    }

    [Fact(DisplayName = "O cabeçalho é reescrito mesmo quando já estava válido")]
    public void CabecalhoEReescritoComOValorFinal()
    {
        Envelope envelope = ComCabecalho("id-da-mensagem");
        envelope.Headers["outro"] = "preservado";

        using IDisposable escopo = CorrelationIdEnvelopeMiddleware.Before(envelope, Ambiente(null));

        envelope.Headers[Cabecalho].Should().Be("id-da-mensagem");
        envelope.Headers["outro"].Should().Be("preservado");
    }

    [Fact(DisplayName = "Identificador com 128 caracteres é aceito e com 129 é trocado")]
    public void LimiteDeTamanhoDoIdentificador()
    {
        string limite = new('b', 128);
        Envelope dentro = ComCabecalho(limite);
        Envelope fora = ComCabecalho(new string('b', 129));

        using IDisposable escopoDentro = CorrelationIdEnvelopeMiddleware.Before(dentro, Ambiente(null));
        using IDisposable escopoFora = CorrelationIdEnvelopeMiddleware.Before(fora, Ambiente(null));

        dentro.Headers[Cabecalho].Should().Be(limite);
        Guid.TryParseExact(fora.Headers[Cabecalho], "D", out _).Should().BeTrue();
    }

    [Fact(DisplayName = "A propriedade de log CorrelationId vale até o Finally liberar o escopo")]
    public void FinallyLiberaOEscopo()
    {
        EscopoDeLogLido leitor = new();
        IDisposable escopo = CorrelationIdEnvelopeMiddleware.Before(ComCabecalho("id-da-mensagem"), Ambiente(null));

        string? durante = leitor.CorrelationIdAgora();
        CorrelationIdEnvelopeMiddleware.Finally(escopo);

        durante.Should().Be("id-da-mensagem");
        leitor.CorrelationIdAgora().Should().BeNull();
    }

    [Fact(DisplayName = "A tag correlation_id é gravada no rastreamento em andamento")]
    public void TagNoRastreamento()
    {
        using Activity atividade = new("mensagem-de-teste");
        atividade.Start();

        using IDisposable escopo = CorrelationIdEnvelopeMiddleware.Before(ComCabecalho("id-da-mensagem"), Ambiente(null));

        atividade.GetTagItem("correlation_id").Should().Be("id-da-mensagem");
    }

    [Fact(DisplayName = "Envelope ou acessor nulo é recusado e o nome do cabeçalho é o publicado")]
    public void ArgumentosNulosSaoRecusados()
    {
        Action semEnvelope = () => CorrelationIdEnvelopeMiddleware.Before(null!, Ambiente(null));
        Action semAcessor = () => CorrelationIdEnvelopeMiddleware.Before(new Envelope(), null!);

        semEnvelope.Should().Throw<ArgumentNullException>();
        semAcessor.Should().Throw<ArgumentNullException>();
        CorrelationIdEnvelopeMiddleware.HeaderName.Should().Be(Cabecalho);
    }

    private static Envelope ComCabecalho(string? valor)
    {
        Envelope envelope = new();
        if (valor is not null)
        {
            envelope.Headers[Cabecalho] = valor;
        }

        return envelope;
    }

    private static ICorrelationIdAccessor Ambiente(string? correlationId)
    {
        ICorrelationIdAccessor acessor = Substitute.For<ICorrelationIdAccessor>();
        acessor.CorrelationId.Returns(correlationId);
        return acessor;
    }
}
