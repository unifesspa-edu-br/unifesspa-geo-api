namespace Unifesspa.Geo.Infrastructure.Core.Cryptography;

using Microsoft.Extensions.Options;

/// <summary>
/// Valida <see cref="EncryptionOptions"/> no boot da aplicação para falhar com mensagem
/// específica quando a combinação de Provider + campos dependentes está inconsistente
/// (por exemplo, Provider=local sem LocalKey). Sem este validator a configuração inválida
/// só estouraria na primeira request que toca cifragem, retornando 500 ao consumer.
///
/// Detalhes em <c>docs/guia-config-cifragem.md</c>.
/// </summary>
internal sealed class EncryptionOptionsValidator : IValidateOptions<EncryptionOptions>
{
    private const int RequiredKeyBytes = 32;
    private const string GuideHint = "Detalhes em docs/guia-config-cifragem.md.";
    private const string ProviderAusente =
        $"Geo:Encryption:Provider é obrigatório. Valores aceitos: 'local', 'vault'. {GuideHint}";
    private const string ChaveLocalAusente =
        "Geo:Encryption:LocalKey é obrigatório quando Provider = 'local'. " +
        "Defina via env var GEO__ENCRYPTION__LOCALKEY (Base64, 32 bytes). " +
        "Gere uma chave dev/CI com `head -c 32 /dev/urandom | base64`. " +
        $"{GuideHint}";
    private const string ChaveLocalSemBase64 =
        "Geo:Encryption:LocalKey não é uma string Base64 válida. " +
        "Gere uma chave válida com `head -c 32 /dev/urandom | base64`. " +
        $"{GuideHint}";
    private const string EnderecoDoVaultAusente =
        "Geo:Encryption:VaultAddress é obrigatório quando Provider = 'vault'. " +
        "Exemplo: 'http://platform-vault-uniplus-standalone.vault.svc.cluster.local:8200'. " +
        $"{GuideHint}";
    private const string NenhumMetodoDeAutenticacao =
        "Geo:Encryption requer KubernetesRole (produção) ou VaultToken (testes/dev) quando Provider = 'vault'. " +
        "Configure GEO__ENCRYPTION__KUBERNETESROLE (role K8s auth registrada no Vault) " +
        "ou GEO__ENCRYPTION__VAULTTOKEN (apenas para testes de integração — nunca em produção). " +
        $"{GuideHint}";
    private const string AmbosMetodosDeAutenticacao =
        "Geo:Encryption: KubernetesRole e VaultToken são mutuamente exclusivos. " +
        "Em produção use KubernetesRole; em testes/dev use VaultToken. " +
        $"{GuideHint}";
    private static readonly string[] AllowedProviders = ["local", "vault"];

    public ValidateOptionsResult Validate(string? name, EncryptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Só valida a instância default — projeto não usa named options para cifragem.
        if (!string.IsNullOrEmpty(name) && !string.Equals(name, Options.DefaultName, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Skip;
        }

        string[] failures = [.. FalhasDe(options)];

        return failures.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static IEnumerable<string> FalhasDe(EncryptionOptions options)
    {
        string provider = options.Provider?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(provider))
        {
            return [ProviderAusente];
        }

        if (!IsKnownProvider(provider))
        {
            return [FalhaDoProviderDesconhecido(options.Provider)];
        }

        return FalhasDoProvedorConhecido(options, provider);
    }

    private static IEnumerable<string> FalhasDoProvedorConhecido(EncryptionOptions options, string provider) =>
        provider.Equals("local", StringComparison.OrdinalIgnoreCase)
            ? Presentes(FalhaDaChaveLocal(options.LocalKey))
            : Presentes(FalhaDoEnderecoDoVault(options.VaultAddress), FalhaDoMetodoDeAutenticacao(options));

    private static string FalhaDoProviderDesconhecido(string? provider) =>
        $"Geo:Encryption:Provider inválido: '{provider}'. " +
        $"Valores aceitos: 'local', 'vault'. {GuideHint}";

    private static IEnumerable<string> Presentes(params string?[] falhas) => falhas.OfType<string>();

    private static bool IsKnownProvider(string provider) =>
        AllowedProviders.Any(allowed => provider.Equals(allowed, StringComparison.OrdinalIgnoreCase));

    private static string? FalhaDaChaveLocal(string? chave)
    {
        if (string.IsNullOrWhiteSpace(chave))
        {
            return ChaveLocalAusente;
        }

        byte[]? keyBytes = Base64OuNulo(chave);

        if (keyBytes is null)
        {
            return ChaveLocalSemBase64;
        }

        return keyBytes.Length == RequiredKeyBytes ? null : FalhaDoTamanhoDaChave(keyBytes.Length);
    }

    private static byte[]? Base64OuNulo(string valor)
    {
        try
        {
            return Convert.FromBase64String(valor);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string FalhaDoTamanhoDaChave(int recebido) =>
        $"Geo:Encryption:LocalKey deve ter {RequiredKeyBytes} bytes (256 bits) após decode Base64. " +
        $"Recebido: {recebido} bytes. " +
        $"{GuideHint}";

    private static string? FalhaDoEnderecoDoVault(string? endereco)
    {
        if (string.IsNullOrWhiteSpace(endereco))
        {
            return EnderecoDoVaultAusente;
        }

        return IsAbsoluteHttpUri(endereco) ? null : FalhaDoEnderecoInvalido(endereco);
    }

    private static string FalhaDoEnderecoInvalido(string endereco) =>
        $"Geo:Encryption:VaultAddress inválido: '{endereco}'. " +
        "Deve ser uma URL absoluta com scheme http ou https. " +
        $"{GuideHint}";

    // O auth method é determinado pela configuração validada: KubernetesRole ativa Kubernetes auth,
    // VaultToken ativa token auth. Aceitar ambos definidos forçaria uma escolha implícita no
    // VaultTransitEncryptionService (originalmente uma heurística "K8s se JWT existir, senão token",
    // retirada exatamente por este motivo). Operador escolhe um, explicitamente.
    private static string? FalhaDoMetodoDeAutenticacao(EncryptionOptions options)
    {
        bool hasKubernetesRole = !string.IsNullOrWhiteSpace(options.KubernetesRole);
        bool hasVaultToken = !string.IsNullOrWhiteSpace(options.VaultToken);

        if (!hasKubernetesRole && !hasVaultToken)
        {
            return NenhumMetodoDeAutenticacao;
        }

        return hasKubernetesRole && hasVaultToken ? AmbosMetodosDeAutenticacao : null;
    }

    private static bool IsAbsoluteHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));
}
