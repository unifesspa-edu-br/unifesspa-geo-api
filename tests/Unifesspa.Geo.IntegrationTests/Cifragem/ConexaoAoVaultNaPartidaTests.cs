namespace Unifesspa.Geo.IntegrationTests.Cifragem;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Core.Cryptography;

public sealed class ConexaoAoVaultNaPartidaTests
{
    private const string Endereco = OpcoesDeCifragemParaTeste.EnderecoDoVault;

    [Theory(DisplayName = "Sem endereço do Vault, nulo ou em branco, a criação falha com a mensagem de endereço obrigatório")]
    [InlineData(null)]
    [InlineData("  ")]
    public void Criar_SemEndereco_LancaMensagemDeEnderecoObrigatorio(string? endereco)
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Vault(endereco, "papel-fictício", null);

        MensagemDaFalha(opcoes).Should().Be(
            "Geo:Encryption:VaultAddress é obrigatório quando Provider = 'vault'.");
    }

    [Fact(DisplayName = "O endereço ausente é recusado antes da conferência dos métodos de autenticação")]
    public void Criar_SemEnderecoENemAutenticacao_RecusaOEnderecoPrimeiro()
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Vault(null, null, null);

        MensagemDaFalha(opcoes).Should().StartWith("Geo:Encryption:VaultAddress é obrigatório");
    }

    [Fact(DisplayName = "Papel do Kubernetes e token juntos falham com a mensagem de métodos mutuamente exclusivos")]
    public void Criar_PapelETokenJuntos_LancaMensagemDeMetodosMutuamenteExclusivos()
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Vault(Endereco, "papel-fictício", "token-fictício");

        MensagemDaFalha(opcoes).Should().Be(
            "Geo:Encryption: KubernetesRole e VaultToken são mutuamente exclusivos quando Provider = 'vault'. " +
            "Em produção use KubernetesRole; em testes/dev use VaultToken. " +
            "Ver EncryptionOptionsValidator e docs/guia-config-cifragem.md.");
    }

    [Fact(DisplayName = "Sem papel do Kubernetes nem token a criação falha com a mensagem de nenhum método definido")]
    public void Criar_SemPapelNemToken_LancaMensagemDeNenhumMetodoDefinido()
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Vault(Endereco, " ", null);

        MensagemDaFalha(opcoes).Should().Be(
            "Geo:Encryption: nem KubernetesRole nem VaultToken estão definidos quando Provider = 'vault'. " +
            "Configure GEO__ENCRYPTION__KUBERNETESROLE (produção) ou GEO__ENCRYPTION__VAULTTOKEN (testes/dev). " +
            "Ver EncryptionOptionsValidator e docs/guia-config-cifragem.md.");
    }

    [Theory(DisplayName = "Autenticação pelo Kubernetes com caminho do JWT vazio falha com a mensagem de caminho vazio")]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_CaminhoDoJwtVazio_LancaMensagemDeCaminhoVazio(string caminho)
    {
        EncryptionOptions opcoes = ComPapelEJwtEm(caminho);

        MensagemDaFalha(opcoes).Should().Be(
            "Geo:Encryption:KubernetesJwtPath está vazio. Configure o path do JWT do " +
            "ServiceAccount (default: /var/run/secrets/kubernetes.io/serviceaccount/token).");
    }

    [Fact(DisplayName = "Autenticação pelo Kubernetes com arquivo do JWT inexistente falha citando o caminho")]
    public void Criar_ArquivoDoJwtInexistente_LancaMensagemComOCaminho()
    {
        using ArquivoTemporarioDeTeste arquivo = ArquivoTemporarioDeTeste.Inexistente();

        MensagemDaFalha(ComPapelEJwtEm(arquivo.Caminho)).Should().Be(
            $"JWT do ServiceAccount não encontrado em '{arquivo.Caminho}'. " +
            "Verifique se o ServiceAccount está montado no Pod (automountServiceAccountToken=true) " +
            "e se o caminho Geo:Encryption:KubernetesJwtPath corresponde ao volume do token.");
    }

    [Theory(DisplayName = "Autenticação pelo Kubernetes com arquivo do JWT vazio ou só com espaços falha citando o caminho")]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public void Criar_ArquivoDoJwtSemConteudo_LancaMensagemComOCaminho(string conteudo)
    {
        using ArquivoTemporarioDeTeste arquivo = ArquivoTemporarioDeTeste.ComConteudo(conteudo);

        MensagemDaFalha(ComPapelEJwtEm(arquivo.Caminho)).Should().Be(
            $"JWT do ServiceAccount em '{arquivo.Caminho}' está vazio. " +
            "O kubelet costuma re-popular o token automaticamente; verificar logs do pod e " +
            "o estado do volume projetado do ServiceAccount.");
    }

    [Fact(DisplayName = "Autenticação pelo Kubernetes com arquivo do JWT preenchido cria o serviço sem acessar a rede")]
    public void Criar_ArquivoDoJwtPreenchido_CriaOServico()
    {
        using ArquivoTemporarioDeTeste arquivo = ArquivoTemporarioDeTeste.ComConteudo("jwt-ficticio");

        using VaultTransitEncryptionService servico = Criar(ComPapelEJwtEm(arquivo.Caminho));

        servico.Should().NotBeNull();
    }

    [Fact(DisplayName = "Autenticação por token com endereço informado cria o serviço sem acessar a rede")]
    public void Criar_ComTokenEEndereco_CriaOServico()
    {
        using VaultTransitEncryptionService servico = Criar(
            OpcoesDeCifragemParaTeste.Vault(Endereco, null, "token-fictício"));

        servico.Should().NotBeNull();
    }

    [Fact(DisplayName = "Chave vazia ou texto nulo na cifragem do Vault voltam como tarefa falhada, sem exceção síncrona")]
    public async Task Cifrar_ComArgumentoInvalido_DevolveTarefaFalhada()
    {
        using VaultTransitEncryptionService servico = Criar(
            OpcoesDeCifragemParaTeste.Vault(Endereco, null, "token-fictício"));

        Task<byte[]> chaveVazia = servico.EncryptAsync(string.Empty, [1]);
        Task<byte[]> textoNulo = servico.EncryptAsync("chave", null!);

        await Assert.ThrowsAsync<ArgumentException>(() => chaveVazia);
        await Assert.ThrowsAsync<ArgumentNullException>(() => textoNulo);
    }

    [Fact(DisplayName = "Chave vazia ou cifrado nulo na decifragem do Vault voltam como tarefa falhada, sem exceção síncrona")]
    public async Task Decifrar_ComArgumentoInvalido_DevolveTarefaFalhada()
    {
        using VaultTransitEncryptionService servico = Criar(
            OpcoesDeCifragemParaTeste.Vault(Endereco, null, "token-fictício"));

        Task<byte[]> chaveVazia = servico.DecryptAsync(string.Empty, [1]);
        Task<byte[]> cifradoNulo = servico.DecryptAsync("chave", null!);

        await Assert.ThrowsAsync<ArgumentException>(() => chaveVazia);
        await Assert.ThrowsAsync<ArgumentNullException>(() => cifradoNulo);
    }

    private static EncryptionOptions ComPapelEJwtEm(string caminho)
    {
        EncryptionOptions opcoes = OpcoesDeCifragemParaTeste.Vault(Endereco, "papel-fictício", null);
        opcoes.KubernetesJwtPath = caminho;
        return opcoes;
    }

    private static string MensagemDaFalha(EncryptionOptions opcoes) =>
        Assert.Throws<InvalidOperationException>(() => Criar(opcoes)).Message;

    private static VaultTransitEncryptionService Criar(EncryptionOptions opcoes) =>
        new(Options.Create(opcoes), NullLogger<VaultTransitEncryptionService>.Instance);
}
