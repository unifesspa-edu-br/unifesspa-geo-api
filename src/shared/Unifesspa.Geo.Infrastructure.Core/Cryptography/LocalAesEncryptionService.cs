namespace Unifesspa.Geo.Infrastructure.Core.Cryptography;

using System.Security.Cryptography;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Implementação AES-GCM 256 para dev/CI. Não usar em produção.
/// Formato do ciphertext: nonce (12 bytes) || tag (16 bytes) || dados cifrados.
/// </summary>
internal sealed partial class LocalAesEncryptionService : IGeoEncryptionService
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly byte[] _key;
    private readonly ILogger<LocalAesEncryptionService> _logger;

    public LocalAesEncryptionService(IOptions<EncryptionOptions> options, ILogger<LocalAesEncryptionService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        _key = ChaveDe32Bytes(DecodificarChaveLocal(options.Value.LocalKey
            ?? throw new InvalidOperationException(
                "Geo:Encryption:LocalKey é obrigatório quando Provider = 'local'. " +
                "Defina via env var GEO__ENCRYPTION__LOCALKEY (base64, 32 bytes).")));
    }

    public Task<byte[]> EncryptAsync(string keyName, byte[] plaintext, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);
        ArgumentNullException.ThrowIfNull(plaintext);

        return Task.FromResult(TraducaoDaFalhaDeCifragem.Executar(keyName, () => Cifrar(keyName, plaintext)));
    }

    public Task<byte[]> DecryptAsync(string keyName, byte[] ciphertext, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);
        ArgumentNullException.ThrowIfNull(ciphertext);

        if (ciphertext.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new EncryptionFailureException(keyName,
                new CryptographicException("Ciphertext inválido: tamanho insuficiente."));
        }

        return Task.FromResult(TraducaoDaFalhaDeCifragem.Executar(keyName, () => Decifrar(keyName, ciphertext)));
    }

    private static byte[] DecodificarChaveLocal(string localKey)
    {
        try
        {
            return Convert.FromBase64String(localKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Geo:Encryption:LocalKey não é uma string Base64 válida.", ex);
        }
    }

    private static byte[] ChaveDe32Bytes(byte[] keyBytes)
    {
        if (keyBytes.Length != 32)
        {
            throw new InvalidOperationException(
                $"Geo:Encryption:LocalKey deve ter 32 bytes (256 bits). Recebido: {keyBytes.Length} bytes.");
        }

        return keyBytes;
    }

    private byte[] Cifrar(string keyName, byte[] plaintext)
    {
        byte[] nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSizeBytes];

        using AesGcm aes = new(_key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag,
            associatedData: System.Text.Encoding.UTF8.GetBytes(keyName));

        byte[] result = NonceTagEDados(nonce, tag, ciphertext);

        LogEncrypt(_logger, keyName);
        return result;
    }

    private static byte[] NonceTagEDados(byte[] nonce, byte[] tag, byte[] dados)
    {
        byte[] result = new byte[NonceSizeBytes + TagSizeBytes + dados.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSizeBytes);
        dados.CopyTo(result, NonceSizeBytes + TagSizeBytes);
        return result;
    }

    private byte[] Decifrar(string keyName, byte[] ciphertext)
    {
        ReadOnlySpan<byte> nonce = ciphertext.AsSpan(0, NonceSizeBytes);
        ReadOnlySpan<byte> tag = ciphertext.AsSpan(NonceSizeBytes, TagSizeBytes);
        ReadOnlySpan<byte> encrypted = ciphertext.AsSpan(NonceSizeBytes + TagSizeBytes);

        byte[] plaintext = new byte[encrypted.Length];

        using AesGcm aes = new(_key, TagSizeBytes);
        aes.Decrypt(nonce, encrypted, tag, plaintext,
            associatedData: System.Text.Encoding.UTF8.GetBytes(keyName));

        LogDecrypt(_logger, keyName);
        return plaintext;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Encrypt concluído para chave '{KeyName}'")]
    private static partial void LogEncrypt(ILogger logger, string keyName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Decrypt concluído para chave '{KeyName}'")]
    private static partial void LogDecrypt(ILogger logger, string keyName);
}
