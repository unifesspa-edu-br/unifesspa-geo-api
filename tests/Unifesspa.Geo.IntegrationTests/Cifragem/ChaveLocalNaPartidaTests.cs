namespace Unifesspa.Geo.IntegrationTests.Cifragem;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Core.Cryptography;

public sealed class ChaveLocalNaPartidaTests
{
    [Fact(DisplayName = "Chave local nula impede a criação do serviço com a mensagem de chave obrigatória")]
    public void Criar_ChaveNula_LancaMensagemDeChaveObrigatoria()
    {
        InvalidOperationException falha = Assert.Throws<InvalidOperationException>(() => Criar(null));

        falha.Message.Should().Be(
            "Geo:Encryption:LocalKey é obrigatório quando Provider = 'local'. " +
            "Defina via env var GEO__ENCRYPTION__LOCALKEY (base64, 32 bytes).");
        falha.InnerException.Should().BeNull();
    }

    [Fact(DisplayName = "Chave local que não é Base64 impede a criação do serviço, guardando o erro de formato como causa")]
    public void Criar_ChaveForaDoBase64_LancaMensagemDeBase64InvalidoComCausaDeFormato()
    {
        InvalidOperationException falha = Assert.Throws<InvalidOperationException>(() => Criar("isto não é base64!"));

        falha.Message.Should().Be("Geo:Encryption:LocalKey não é uma string Base64 válida.");
        falha.InnerException.Should().BeOfType<FormatException>();
    }

    [Theory(DisplayName = "Chave local com tamanho diferente de 32 bytes impede a criação do serviço informando o tamanho recebido")]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void Criar_ChaveComTamanhoErrado_LancaMensagemComOTamanhoRecebido(int tamanhoEmBytes)
    {
        string chave = OpcoesDeCifragemParaTeste.ChaveEmBase64De(tamanhoEmBytes);

        InvalidOperationException falha = Assert.Throws<InvalidOperationException>(() => Criar(chave));

        falha.Message.Should().Be(
            $"Geo:Encryption:LocalKey deve ter 32 bytes (256 bits). Recebido: {tamanhoEmBytes} bytes.");
    }

    [Fact(DisplayName = "Chave local vazia passa pela verificação de nulo e falha no tamanho, com 0 bytes recebidos")]
    public void Criar_ChaveVazia_FalhaNoTamanhoComZeroBytes()
    {
        InvalidOperationException falha = Assert.Throws<InvalidOperationException>(() => Criar(string.Empty));

        falha.Message.Should().Be("Geo:Encryption:LocalKey deve ter 32 bytes (256 bits). Recebido: 0 bytes.");
    }

    [Fact(DisplayName = "Chave local com 32 bytes em Base64 cria o serviço")]
    public void Criar_ChaveDe32Bytes_CriaOServico()
    {
        Action criar = () => Criar(OpcoesDeCifragemParaTeste.ChaveEmBase64De(32));

        criar.Should().NotThrow();
    }

    private static LocalAesEncryptionService Criar(string? chaveEmBase64) =>
        new(
            Options.Create(OpcoesDeCifragemParaTeste.Local(chaveEmBase64)),
            NullLogger<LocalAesEncryptionService>.Instance);
}
