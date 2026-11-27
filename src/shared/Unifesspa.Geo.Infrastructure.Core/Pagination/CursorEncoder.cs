namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

using System.Buffers.Text;
using System.Text;
using System.Text.Json;

using Cryptography;

/// <summary>
/// Codifica e decodifica cursores opacos AES-GCM (ADR-0026): JSON do
/// <see cref="CursorPayload"/> cifrado via <see cref="IGeoEncryptionService"/>
/// e codificado em Base64URL.
/// </summary>
public sealed class CursorEncoder
{
    /// <summary>
    /// Chave do Transit que cifra o payload do cursor de paginação.
    /// A política do Vault do Geo precisa conceder encrypt/decrypt nesta chave.
    /// </summary>
    public const string KeyName = "geo-cursor-aesgcm";

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IGeoEncryptionService _encryption;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Construtor único — força <see cref="TimeProvider"/> resolvido via DI a
    /// efetivamente fluir até a verificação de expiração. Um construtor
    /// adicional sem TimeProvider seria silenciosamente preferido por
    /// <c>ActivatorUtilities</c> e ignoraria override de relógio em testes.
    /// </summary>
    public CursorEncoder(IGeoEncryptionService encryption, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _encryption = encryption;
        _timeProvider = timeProvider;
    }

    public async Task<string> EncodeAsync(CursorPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, PayloadJsonOptions);
        byte[] ciphertext = await _encryption.EncryptAsync(KeyName, plaintext, cancellationToken).ConfigureAwait(false);
        return Base64Url.EncodeToString(ciphertext);
    }

    public async Task<CursorDecodeResult> TryDecodeAsync(string token, CancellationToken cancellationToken = default)
    {
        byte[]? cifrado = DecodificarBase64Url(token);
        byte[]? claro = cifrado is null ? null : await DecifrarAsync(cifrado, cancellationToken).ConfigureAwait(false);
        CursorPayload? payload = claro is null ? null : LerPayload(claro);

        if (payload is null)
            return CursorDecodeResult.Invalid();

        return payload.ExpiresAt <= _timeProvider.GetUtcNow()
            ? CursorDecodeResult.Expired(payload)
            : CursorDecodeResult.Success(payload);
    }

    // As três etapas devolvem null na falha: cursor ilegível, adulterado ou sem os campos
    // obrigatórios é sempre o mesmo resultado para quem chama (Invalid), sem expor o motivo.
    private static byte[]? DecodificarBase64Url(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            return Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task<byte[]?> DecifrarAsync(byte[] cifrado, CancellationToken cancellationToken)
    {
        try
        {
            return await _encryption.DecryptAsync(KeyName, cifrado, cancellationToken).ConfigureAwait(false);
        }
        catch (EncryptionFailureException)
        {
            return null;
        }
    }

    private static CursorPayload? LerPayload(byte[] claro)
    {
        try
        {
            CursorPayload? payload = JsonSerializer.Deserialize<CursorPayload>(claro, PayloadJsonOptions);
            return string.IsNullOrEmpty(payload?.After) || string.IsNullOrEmpty(payload.ResourceTag) ? null : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
