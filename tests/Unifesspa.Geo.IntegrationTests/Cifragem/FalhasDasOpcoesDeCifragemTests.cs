namespace Unifesspa.Geo.IntegrationTests.Cifragem;

using AwesomeAssertions;

using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Core.Cryptography;

public sealed class FalhasDasOpcoesDeCifragemTests
{
    private const string Guia = "Detalhes em docs/guia-config-cifragem.md.";
    private const string FalhaDoEndereco =
        "Geo:Encryption:VaultAddress é obrigatório quando Provider = 'vault'. " +
        "Exemplo: 'http://platform-vault-uniplus-standalone.vault.svc.cluster.local:8200'. " + Guia;
    private const string FalhaDaAutenticacaoAusente =
        "Geo:Encryption requer KubernetesRole (produção) ou VaultToken (testes/dev) quando Provider = 'vault'. " +
        "Configure GEO__ENCRYPTION__KUBERNETESROLE (role K8s auth registrada no Vault) " +
        "ou GEO__ENCRYPTION__VAULTTOKEN (apenas para testes de integração — nunca em produção). " + Guia;
    private const string FalhaDaAutenticacaoDupla =
        "Geo:Encryption: KubernetesRole e VaultToken são mutuamente exclusivos. " +
        "Em produção use KubernetesRole; em testes/dev use VaultToken. " + Guia;

    [Theory(DisplayName = "Provedor ausente ou em branco falha com a mensagem de provedor obrigatório")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validar_ProvedorAusenteOuEmBranco_FalhaComProvedorObrigatorio(string? provedor)
    {
        EncryptionOptions opcoes = new() { Provider = provedor! };

        FalhasDe(opcoes).Should().Equal(
            "Geo:Encryption:Provider é obrigatório. Valores aceitos: 'local', 'vault'. " + Guia);
    }

    [Fact(DisplayName = "Provedor desconhecido falha citando o valor como foi informado, sem aparar")]
    public void Validar_ProvedorDesconhecido_FalhaCitandoOValorInformado()
    {
        EncryptionOptions opcoes = new() { Provider = " Kms " };

        FalhasDe(opcoes).Should().Equal(
            "Geo:Encryption:Provider inválido: ' Kms '. Valores aceitos: 'local', 'vault'. " + Guia);
    }

    [Theory(DisplayName = "Provedor aparado e sem diferenciar maiúsculas de minúsculas é aceito")]
    [InlineData(" LOCAL ")]
    [InlineData("Local")]
    [InlineData("local")]
    public void Validar_ProvedorLocalComEspacosECaixaDiferente_Passa(string provedor)
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Local(OpcoesDeCifragemParaTeste.ChaveEmBase64De(32));
        opcoes.Provider = provedor;

        Validar(opcoes).Succeeded.Should().BeTrue();
    }

    [Fact(DisplayName = "Provedor vault com caixa diferente e espaços é validado como vault")]
    public void Validar_ProvedorVaultComEspacosECaixaDiferente_ExigeOsCamposDoVault()
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Vault(null, null, null);
        opcoes.Provider = " VAULT ";

        FalhasDe(opcoes).Should().Equal(FalhaDoEndereco, FalhaDaAutenticacaoAusente);
    }

    [Theory(DisplayName = "Provedor local sem chave, nula ou em branco, falha com a mensagem de chave obrigatória")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validar_LocalSemChave_FalhaComChaveObrigatoria(string? chave)
    {
        FalhasDe(OpcoesDeCifragemParaTeste.Local(chave)).Should().Equal(
            "Geo:Encryption:LocalKey é obrigatório quando Provider = 'local'. " +
            "Defina via env var GEO__ENCRYPTION__LOCALKEY (Base64, 32 bytes). " +
            "Gere uma chave dev/CI com `head -c 32 /dev/urandom | base64`. " + Guia);
    }

    [Fact(DisplayName = "Provedor local com chave fora do Base64 falha só com a mensagem de Base64 inválido")]
    public void Validar_LocalComChaveForaDoBase64_FalhaSoComBase64Invalido()
    {
        FalhasDe(OpcoesDeCifragemParaTeste.Local("isto não é base64!")).Should().Equal(
            "Geo:Encryption:LocalKey não é uma string Base64 válida. " +
            "Gere uma chave válida com `head -c 32 /dev/urandom | base64`. " + Guia);
    }

    [Theory(DisplayName = "Provedor local com chave de tamanho diferente de 32 bytes falha só com a mensagem do tamanho recebido")]
    [InlineData(16)]
    [InlineData(33)]
    public void Validar_LocalComChaveDeTamanhoErrado_FalhaSoComOTamanhoRecebido(int tamanhoEmBytes)
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Local(
            OpcoesDeCifragemParaTeste.ChaveEmBase64De(tamanhoEmBytes));

        FalhasDe(opcoes).Should().Equal(
            "Geo:Encryption:LocalKey deve ter 32 bytes (256 bits) após decode Base64. " +
            $"Recebido: {tamanhoEmBytes} bytes. " + Guia);
    }

    [Fact(DisplayName = "Provedor local com chave de 32 bytes em Base64 passa")]
    public void Validar_LocalComChaveDe32Bytes_Passa()
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Local(OpcoesDeCifragemParaTeste.ChaveEmBase64De(32));

        Validar(opcoes).Succeeded.Should().BeTrue();
    }

    [Theory(DisplayName = "Provedor vault sem endereço, nulo ou em branco, falha com a mensagem de endereço obrigatório")]
    [InlineData(null)]
    [InlineData("  ")]
    public void Validar_VaultSemEndereco_FalhaComEnderecoObrigatorio(string? endereco)
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Vault(endereco, null, "token-fictício");

        FalhasDe(opcoes).Should().Equal(FalhaDoEndereco);
    }

    [Theory(DisplayName = "Provedor vault com endereço que não é URL absoluta http ou https falha com a mensagem de endereço inválido")]
    [InlineData("vault.exemplo.invalid")]
    [InlineData("ftp://vault.exemplo.invalid")]
    public void Validar_VaultComEnderecoInvalido_FalhaCitandoOEndereco(string endereco)
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Vault(endereco, null, "token-fictício");

        FalhasDe(opcoes).Should().Equal(
            $"Geo:Encryption:VaultAddress inválido: '{endereco}'. " +
            "Deve ser uma URL absoluta com scheme http ou https. " + Guia);
    }

    [Theory(DisplayName = "Provedor vault com endereço http ou https e só um método de autenticação passa")]
    [InlineData("http://vault.exemplo.invalid:8200", "papel-fictício", null)]
    [InlineData("https://vault.exemplo.invalid", null, "token-fictício")]
    public void Validar_VaultComEnderecoEUmMetodoDeAutenticacao_Passa(string endereco, string? papel, string? token)
    {
        Validar(OpcoesDeCifragemParaTeste.Vault(endereco, papel, token)).Succeeded.Should().BeTrue();
    }

    [Fact(DisplayName = "Provedor vault sem papel nem token falha com a mensagem de autenticação ausente")]
    public void Validar_VaultSemPapelNemToken_FalhaComAutenticacaoAusente()
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Vault(OpcoesDeCifragemParaTeste.EnderecoDoVault, " ", "");

        FalhasDe(opcoes).Should().Equal(FalhaDaAutenticacaoAusente);
    }

    [Fact(DisplayName = "Provedor vault com papel e token juntos falha com a mensagem de métodos mutuamente exclusivos")]
    public void Validar_VaultComPapelETokenJuntos_FalhaComAutenticacaoDupla()
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Vault(
            OpcoesDeCifragemParaTeste.EnderecoDoVault, "papel-fictício", "token-fictício");

        FalhasDe(opcoes).Should().Equal(FalhaDaAutenticacaoDupla);
    }

    [Fact(DisplayName = "Provedor vault acumula a falha do endereço e a da autenticação, nessa ordem")]
    public void Validar_VaultSemEnderecoENemAutenticacao_AcumulaEnderecoDepoisAutenticacao()
    {
        FalhasDe(OpcoesDeCifragemParaTeste.Vault(null, null, null))
            .Should().Equal(FalhaDoEndereco, FalhaDaAutenticacaoAusente);
    }

    [Fact(DisplayName = "Provedor vault com endereço inválido e autenticação dupla acumula as duas falhas, nessa ordem")]
    public void Validar_VaultComEnderecoInvalidoEAutenticacaoDupla_AcumulaEnderecoDepoisAutenticacao()
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Vault("sem-esquema", "papel-fictício", "token-fictício");

        FalhasDe(opcoes).Should().Equal(
            "Geo:Encryption:VaultAddress inválido: 'sem-esquema'. " +
            "Deve ser uma URL absoluta com scheme http ou https. " + Guia,
            FalhaDaAutenticacaoDupla);
    }

    [Fact(DisplayName = "Opção nomeada diferente da padrão é ignorada, mesmo inválida")]
    public void Validar_OpcaoNomeadaDiferenteDaPadrao_EIgnorada()
    {
        ValidateOptionsResult resultado = new EncryptionOptionsValidator()
            .Validate("outra", OpcoesDeCifragemParaTeste.Local(null));

        resultado.Skipped.Should().BeTrue();
    }

    [Theory(DisplayName = "Opção sem nome ou com o nome padrão é validada")]
    [InlineData(null)]
    [InlineData("")]
    public void Validar_OpcaoSemNomeOuComNomePadrao_EValidada(string? nome)
    {
        ValidateOptionsResult resultado = new EncryptionOptionsValidator()
            .Validate(nome, OpcoesDeCifragemParaTeste.Local(null));

        resultado.Failed.Should().BeTrue();
    }

    private static ValidateOptionsResult Validar(EncryptionOptions opcoes) =>
        new EncryptionOptionsValidator().Validate(Options.DefaultName, opcoes);

    private static IReadOnlyList<string> FalhasDe(EncryptionOptions opcoes) =>
        [.. Validar(opcoes).Failures ?? []];
}
