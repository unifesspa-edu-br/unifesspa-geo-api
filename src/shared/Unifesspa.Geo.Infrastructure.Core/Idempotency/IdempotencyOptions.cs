namespace Unifesspa.Geo.Infrastructure.Core.Idempotency;

/// <summary>
/// Configuração do cache de idempotência (ADR-0027). Bind a partir da seção
/// <c>Geo:Idempotency</c>; defaults sensatos garantem que projetos sem
/// configuração explícita rodam imediatamente.
/// </summary>
public sealed record IdempotencyOptions
{
    public const string SectionName = "Geo:Idempotency";

    /// <summary>
    /// Chave do Transit que cifra o response body armazenado da idempotência.
    /// A política do Vault do Geo precisa conceder encrypt/decrypt nesta chave.
    /// </summary>
    public const string EncryptionKeyName = "geo-idempotency-aesgcm";

    /// <summary>TTL da entrada do cache. ADR-0027 fixa 24h como teto.</summary>
    public TimeSpan Ttl { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Tamanho máximo do request body aceito (rejeita 413 antes do hash).</summary>
    public long MaxBodyBytes { get; init; } = 1L * 1024 * 1024; // 1 MiB
}
