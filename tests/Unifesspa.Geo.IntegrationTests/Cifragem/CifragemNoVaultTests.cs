namespace Unifesspa.Geo.IntegrationTests.Cifragem;

using System.Text;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Core.Cryptography;
using Unifesspa.Geo.IntegrationTests.Fixtures.Hosting;

public sealed class CifragemNoVaultTests : IClassFixture<VaultContainerFixture>
{
    private static readonly byte[] Texto = Encoding.UTF8.GetBytes("Marabá, Pará");

    private readonly VaultContainerFixture _vault;

    public CifragemNoVaultTests(VaultContainerFixture vault) => _vault = vault;

    [Fact(DisplayName = "Cifrar e decifrar no Vault com a mesma chave devolve o texto original")]
    public async Task IdaEVolta_MesmaChave_DevolveOTextoOriginal()
    {
        string chave = await CriarChaveNoVaultAsync();
        using VaultTransitEncryptionService servico = CriarServico();

        byte[] cifrado = await servico.EncryptAsync(chave, Texto);
        byte[] decifrado = await servico.DecryptAsync(chave, cifrado);

        decifrado.Should().Equal(Texto);
    }

    [Fact(DisplayName = "O texto cifrado pelo Vault é o texto do transit em UTF-8, começando por 'vault:v1:'")]
    public async Task Cifrar_NoVault_DevolveTextoDoTransitComPrefixoDaVersaoUm()
    {
        string chave = await CriarChaveNoVaultAsync();
        using VaultTransitEncryptionService servico = CriarServico();

        byte[] cifrado = await servico.EncryptAsync(chave, Texto);

        Encoding.UTF8.GetString(cifrado).Should().StartWith("vault:v1:");
    }

    [Fact(DisplayName = "Decifrar com uma chave que não existe no Vault lança falha de criptografia")]
    public async Task Decifrar_ChaveInexistenteNoVault_LancaFalhaDeCriptografia()
    {
        string chaveInexistente = $"geo-inexistente-{Guid.NewGuid():N}";
        using VaultTransitEncryptionService servico = CriarServico();
        byte[] cifrado = Encoding.UTF8.GetBytes("vault:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        EncryptionFailureException falha = await Assert.ThrowsAsync<EncryptionFailureException>(
            () => servico.DecryptAsync(chaveInexistente, cifrado));

        falha.KeyName.Should().Be(chaveInexistente);
    }

    private async Task<string> CriarChaveNoVaultAsync()
    {
        string chave = $"geo-teste-{Guid.NewGuid():N}";
        await _vault.EnsureKeyExistsAsync(chave);
        return chave;
    }

    private VaultTransitEncryptionService CriarServico() =>
        new(
            Options.Create(OpcoesDeCifragemParaTeste.Vault(
                _vault.VaultAddress, null, VaultContainerFixture.RootToken)),
            NullLogger<VaultTransitEncryptionService>.Instance);
}
