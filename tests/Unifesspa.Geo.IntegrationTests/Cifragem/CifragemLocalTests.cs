namespace Unifesspa.Geo.IntegrationTests.Cifragem;

using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Core.Cryptography;

public sealed class CifragemLocalTests
{
    private const string NomeDaChave = "chave-de-teste";
    private const int TamanhoDoNonce = 12;
    private const int TamanhoDaTag = 16;
    private static readonly byte[] Texto = Encoding.UTF8.GetBytes("Marabá, Pará");

    [Fact(DisplayName = "Cifrar e decifrar com a mesma chave devolve o texto original")]
    public async Task IdaEVolta_MesmaChave_DevolveOTextoOriginal()
    {
        LocalAesEncryptionService servico = CriarServico();

        byte[] cifrado = await servico.EncryptAsync(NomeDaChave, Texto);
        byte[] decifrado = await servico.DecryptAsync(NomeDaChave, cifrado);

        decifrado.Should().Equal(Texto);
    }

    [Fact(DisplayName = "O texto cifrado tem nonce de 12 bytes, tag de 16 bytes e dados, nessa ordem, com o nome da chave como dado associado")]
    public async Task Cifrar_FormatoNonceTagDados_DecifraComAesGcmEDadoAssociadoDoNomeDaChave()
    {
        byte[] cifrado = await CriarServico().EncryptAsync(NomeDaChave, Texto);

        cifrado.Length.Should().Be(TamanhoDoNonce + TamanhoDaTag + Texto.Length);
        DecifrarComAesGcm(cifrado, NomeDaChave).Should().Equal(Texto);
    }

    [Fact(DisplayName = "Cada cifragem do mesmo texto usa um nonce diferente")]
    public async Task Cifrar_MesmoTextoDuasVezes_UsaNoncesDiferentes()
    {
        LocalAesEncryptionService servico = CriarServico();

        byte[] primeiro = await servico.EncryptAsync(NomeDaChave, Texto);
        byte[] segundo = await servico.EncryptAsync(NomeDaChave, Texto);

        primeiro.Take(TamanhoDoNonce).Should().NotEqual(segundo.Take(TamanhoDoNonce));
    }

    [Fact(DisplayName = "Texto vazio cifrado tem exatamente 28 bytes e decifra para vazio")]
    public async Task IdaEVolta_TextoVazio_TemTamanhoMinimoEDecifraParaVazio()
    {
        LocalAesEncryptionService servico = CriarServico();

        byte[] cifrado = await servico.EncryptAsync(NomeDaChave, []);
        byte[] decifrado = await servico.DecryptAsync(NomeDaChave, cifrado);

        cifrado.Length.Should().Be(TamanhoDoNonce + TamanhoDaTag);
        decifrado.Should().BeEmpty();
    }

    [Fact(DisplayName = "Decifrar um texto adulterado lança falha de criptografia com a falha criptográfica como causa")]
    public async Task Decifrar_TextoAdulterado_LancaFalhaDeCriptografia()
    {
        LocalAesEncryptionService servico = CriarServico();
        byte[] cifrado = await servico.EncryptAsync(NomeDaChave, Texto);
        cifrado[^1] ^= 0xFF;

        EncryptionFailureException falha = await Assert.ThrowsAsync<EncryptionFailureException>(
            () => servico.DecryptAsync(NomeDaChave, cifrado));

        falha.InnerException.Should().BeAssignableTo<CryptographicException>();
        falha.KeyName.Should().Be(NomeDaChave);
    }

    [Fact(DisplayName = "Decifrar com um nome de chave diferente do usado na cifragem lança falha de criptografia")]
    public async Task Decifrar_NomeDeChaveDiferente_LancaFalhaDeCriptografia()
    {
        LocalAesEncryptionService servico = CriarServico();
        byte[] cifrado = await servico.EncryptAsync(NomeDaChave, Texto);

        EncryptionFailureException falha = await Assert.ThrowsAsync<EncryptionFailureException>(
            () => servico.DecryptAsync("outra-chave", cifrado));

        falha.KeyName.Should().Be("outra-chave");
    }

    [Fact(DisplayName = "Decifrar um texto com menos de 28 bytes lança falha de criptografia com a causa 'Ciphertext inválido: tamanho insuficiente.'")]
    public async Task Decifrar_TextoCurto_LancaFalhaComCausaDeTamanhoInsuficiente()
    {
        EncryptionFailureException falha = await Assert.ThrowsAsync<EncryptionFailureException>(
            () => CriarServico().DecryptAsync(NomeDaChave, new byte[27]));

        falha.Message.Should().Be("Falha na operação de criptografia para a chave 'chave-de-teste'.");
        falha.InnerException.Should().BeOfType<CryptographicException>()
            .Which.Message.Should().Be("Ciphertext inválido: tamanho insuficiente.");
    }

    private static LocalAesEncryptionService CriarServico() =>
        new(
            Options.Create(OpcoesDeCifragemParaTeste.Local(OpcoesDeCifragemParaTeste.ChaveEmBase64De(32))),
            NullLogger<LocalAesEncryptionService>.Instance);

    private static byte[] DecifrarComAesGcm(byte[] cifrado, string nomeDaChave)
    {
        ReadOnlySpan<byte> nonce = cifrado.AsSpan(0, TamanhoDoNonce);
        ReadOnlySpan<byte> tag = cifrado.AsSpan(TamanhoDoNonce, TamanhoDaTag);
        ReadOnlySpan<byte> dados = cifrado.AsSpan(TamanhoDoNonce + TamanhoDaTag);
        byte[] decifrado = new byte[dados.Length];

        using AesGcm aes = new(OpcoesDeCifragemParaTeste.ChaveDe(32), TamanhoDaTag);
        aes.Decrypt(nonce, dados, tag, decifrado, Encoding.UTF8.GetBytes(nomeDaChave));
        return decifrado;
    }
}
