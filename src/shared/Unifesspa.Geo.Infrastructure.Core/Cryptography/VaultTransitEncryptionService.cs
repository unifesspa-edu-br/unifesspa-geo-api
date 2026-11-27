namespace Unifesspa.Geo.Infrastructure.Core.Cryptography;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VaultSharp;
using VaultSharp.V1.Commons;
using VaultSharp.V1.SecretsEngines.Transit;

/// <summary>
/// Implementação de produção via HashiCorp Vault transit engine.
/// Chaves nunca saem do Vault; autenticação via Kubernetes auth method.
/// </summary>
internal sealed partial class VaultTransitEncryptionService : IGeoEncryptionService, IDisposable
{
    private readonly ConexaoAoVault _conexao;
    private readonly string _transitMount;
    private readonly ILogger<VaultTransitEncryptionService> _logger;

    public VaultTransitEncryptionService(IOptions<EncryptionOptions> options, ILogger<VaultTransitEncryptionService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _conexao = new ConexaoAoVault(options.Value);
        _transitMount = options.Value.VaultTransitMount;
    }

    public async Task<byte[]> EncryptAsync(string keyName, byte[] plaintext, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);
        ArgumentNullException.ThrowIfNull(plaintext);

        // VaultSharp 1.17.5.1 Transit não expõe CancellationToken — parâmetro recebido mas não propagável.
        return await TraducaoDaFalhaDeCifragem.ExecutarAsync(keyName, () => _conexao.ExecutarComReautenticacaoAsync(
            vault => CifrarNoTransitAsync(vault, keyName, plaintext),
            () => LogAuthRefresh(_logger, keyName))).ConfigureAwait(false);
    }

    public async Task<byte[]> DecryptAsync(string keyName, byte[] ciphertext, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);
        ArgumentNullException.ThrowIfNull(ciphertext);

        // VaultSharp 1.17.5.1 Transit não expõe CancellationToken — parâmetro recebido mas não propagável.
        return await TraducaoDaFalhaDeCifragem.ExecutarAsync(keyName, () => _conexao.ExecutarComReautenticacaoAsync(
            vault => DecifrarNoTransitAsync(vault, keyName, ciphertext),
            () => LogAuthRefresh(_logger, keyName))).ConfigureAwait(false);
    }

    public void Dispose() => _conexao.Dispose();

    private async Task<byte[]> CifrarNoTransitAsync(VaultClient vault, string keyName, byte[] plaintext)
    {
        string base64Plain = Convert.ToBase64String(plaintext);
        EncryptRequestOptions request = new() { Base64EncodedPlainText = base64Plain };

        Secret<EncryptionResponse> response = await vault.V1.Secrets.Transit
            .EncryptAsync(keyName, request, _transitMount)
            .ConfigureAwait(false);

        LogEncrypt(_logger, keyName);
        return System.Text.Encoding.UTF8.GetBytes(response.Data.CipherText);
    }

    private async Task<byte[]> DecifrarNoTransitAsync(VaultClient vault, string keyName, byte[] ciphertext)
    {
        string vaultCiphertext = System.Text.Encoding.UTF8.GetString(ciphertext);
        DecryptRequestOptions request = new() { CipherText = vaultCiphertext };

        Secret<DecryptionResponse> response = await vault.V1.Secrets.Transit
            .DecryptAsync(keyName, request, _transitMount)
            .ConfigureAwait(false);

        LogDecrypt(_logger, keyName);
        return Convert.FromBase64String(response.Data.Base64EncodedPlainText);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vault encrypt concluído para chave '{KeyName}'")]
    private static partial void LogEncrypt(ILogger logger, string keyName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vault decrypt concluído para chave '{KeyName}'")]
    private static partial void LogDecrypt(ILogger logger, string keyName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vault retornou 403 para chave '{KeyName}'; recriando cliente com JWT atualizado e repetindo")]
    private static partial void LogAuthRefresh(ILogger logger, string keyName);
}
