namespace Unifesspa.Geo.IntegrationTests.Correlacao;

using System.Diagnostics;

using AwesomeAssertions;

using Microsoft.Extensions.Primitives;

using NSubstitute;

using Unifesspa.Geo.Infrastructure.Core.Middleware;

/// <summary>
/// O identificador de correlação da requisição: o válido enviado pelo cliente é preservado e devolvido na resposta;
/// o inválido é trocado por um identificador novo; o valor segue para o escopo de log e para o rastreamento.
/// </summary>
public sealed class CorrelacaoNaRequisicaoTests
{
    private const string Cabecalho = "X-Correlation-Id";

    [Theory(DisplayName = "Identificador válido do cliente é preservado e devolvido na resposta")]
    [InlineData("abc-123_X.y")]
    [InlineData("a")]
    public async Task IdentificadorValidoEPreservado(string enviado)
    {
        ICorrelationIdWriter escritor = Substitute.For<ICorrelationIdWriter>();

        ExecucaoDaCorrelacao execucao = await ExecucaoDaCorrelacao.RodarAsync(enviado, escritor);

        escritor.Received(1).SetCorrelationId(enviado);
        execucao.CabecalhoDaResposta.Should().Be(enviado);
    }

    [Fact(DisplayName = "Identificador com exatamente 128 caracteres é válido")]
    public async Task IdentificadorCom128CaracteresEValido()
    {
        string limite = new('a', 128);

        ExecucaoDaCorrelacao execucao = await ExecucaoDaCorrelacao.RodarAsync(limite, Substitute.For<ICorrelationIdWriter>());

        execucao.CabecalhoDaResposta.Should().Be(limite);
    }

    [Theory(DisplayName = "Identificador com espaço ou com caractere fora do permitido vira um GUID novo")]
    [InlineData("com espaco")]
    [InlineData("com;ponto-e-virgula")]
    [InlineData("acentuação")]
    [InlineData("")]
    public async Task IdentificadorInvalidoViraGuid(string enviado)
    {
        ExecucaoDaCorrelacao execucao = await ExecucaoDaCorrelacao.RodarAsync(enviado, Substitute.For<ICorrelationIdWriter>());

        Guid.TryParseExact(execucao.CabecalhoDaResposta, "D", out _).Should().BeTrue();
        execucao.CabecalhoDaResposta.Should().NotBe(enviado);
    }

    [Fact(DisplayName = "Identificador com 129 caracteres vira um GUID novo")]
    public async Task IdentificadorCom129CaracteresViraGuid()
    {
        string longo = new('a', 129);

        ExecucaoDaCorrelacao execucao = await ExecucaoDaCorrelacao.RodarAsync(longo, Substitute.For<ICorrelationIdWriter>());

        Guid.TryParseExact(execucao.CabecalhoDaResposta, "D", out _).Should().BeTrue();
    }

    [Fact(DisplayName = "Cabeçalho repetido, com dois valores, vira um GUID novo")]
    public async Task CabecalhoComDoisValoresViraGuid()
    {
        StringValues doisValores = new(["id-um", "id-dois"]);

        ExecucaoDaCorrelacao execucao = await ExecucaoDaCorrelacao.RodarAsync(doisValores, Substitute.For<ICorrelationIdWriter>());

        Guid.TryParseExact(execucao.CabecalhoDaResposta, "D", out _).Should().BeTrue();
    }

    [Fact(DisplayName = "Sem cabeçalho, gera um GUID novo, grava no escritor e devolve na resposta")]
    public async Task SemCabecalhoGeraGuidNovo()
    {
        ICorrelationIdWriter escritor = Substitute.For<ICorrelationIdWriter>();

        ExecucaoDaCorrelacao execucao = await ExecucaoDaCorrelacao.RodarAsync(StringValues.Empty, escritor);

        Guid.TryParseExact(execucao.CabecalhoDaResposta, "D", out _).Should().BeTrue();
        escritor.Received(1).SetCorrelationId(execucao.CabecalhoDaResposta!);
    }

    [Fact(DisplayName = "A propriedade de log CorrelationId vale durante a requisição e some depois")]
    public async Task PropriedadeDeLogVaiSoDuranteARequisicao()
    {
        EscopoDeLogLido leitor = new();
        string? durante = null;

        await ExecucaoDaCorrelacao.RodarAsync(
            "id-do-cliente",
            Substitute.For<ICorrelationIdWriter>(),
            _ =>
            {
                durante = leitor.CorrelationIdAgora();
                return Task.CompletedTask;
            });

        durante.Should().Be("id-do-cliente");
        leitor.CorrelationIdAgora().Should().BeNull();
    }

    [Fact(DisplayName = "A tag correlation_id é gravada no rastreamento em andamento")]
    public async Task TagNoRastreamento()
    {
        using Activity atividade = new("requisicao-de-teste");
        atividade.Start();

        await ExecucaoDaCorrelacao.RodarAsync("id-do-cliente", Substitute.For<ICorrelationIdWriter>());

        atividade.GetTagItem("correlation_id").Should().Be("id-do-cliente");
        CorrelationIdMiddleware.ActivityTagName.Should().Be("correlation_id");
    }

    [Fact(DisplayName = "Sem rastreamento em andamento a requisição segue normalmente")]
    public async Task SemRastreamentoARequisicaoSegue()
    {
        Activity.Current.Should().BeNull();

        ExecucaoDaCorrelacao execucao = await ExecucaoDaCorrelacao.RodarAsync("id-do-cliente", Substitute.For<ICorrelationIdWriter>());

        execucao.ProximoFoiChamado.Should().BeTrue();
    }

    [Fact(DisplayName = "O nome do cabeçalho, da propriedade de log e o limite são os publicados")]
    public void ConstantesPublicadas()
    {
        CorrelationIdMiddleware.HeaderName.Should().Be(Cabecalho);
        CorrelationIdMiddleware.LogContextProperty.Should().Be("CorrelationId");
        CorrelationIdMiddleware.MaxCorrelationIdLength.Should().Be(128);
    }
}
