namespace Unifesspa.Geo.IntegrationTests.Cifragem;

using Unifesspa.Geo.Infrastructure.Core.Cryptography;

/// <summary>
/// Monta opções de cifragem com valores fictícios para os testes da cifragem.
/// A chave é uma sequência fixa de bytes (0, 1, 2, ...), nunca uma chave real.
/// </summary>
internal static class OpcoesDeCifragemParaTeste
{
    public const string EnderecoDoVault = "http://vault.exemplo.invalid:8200";

    public static byte[] ChaveDe(int tamanhoEmBytes) =>
        [.. Enumerable.Range(0, tamanhoEmBytes).Select(posicao => (byte)posicao)];

    public static string ChaveEmBase64De(int tamanhoEmBytes) =>
        Convert.ToBase64String(ChaveDe(tamanhoEmBytes));

    public static EncryptionOptions Local(string? chaveEmBase64) =>
        new() { Provider = "local", LocalKey = chaveEmBase64 };

    public static EncryptionOptions Vault(string? endereco, string? papelKubernetes, string? token) =>
        new()
        {
            Provider = "vault",
            VaultAddress = endereco,
            KubernetesRole = papelKubernetes,
            VaultToken = token,
        };
}
