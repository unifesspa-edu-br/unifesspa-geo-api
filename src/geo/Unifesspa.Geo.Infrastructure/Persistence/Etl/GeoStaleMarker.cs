namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;

using Npgsql;

/// <summary>
/// Política de linhas obsoletas (stale) da recarga periódica (Story #674, CA-04): ao
/// aplicar uma versão, as linhas de reference data que <strong>não foram revistas nesta
/// carga</strong> são marcadas <c>vigente = false</c> — sem remoção física (rastreabilidade
/// de CEP/logradouro retirado). Não revista é: <c>versao_dataset</c> anterior à aplicada
/// (comparação lexicográfica, válida porque AAAAMM tem comprimento fixo) ou, na recarga da
/// <em>mesma</em> versão, <c>updated_at</c>/<c>created_at</c> anterior ao início da carga —
/// os importadores gravam <c>updated_at</c> em toda linha que revisam.
/// </summary>
/// <remarks>
/// O predicado pressupõe versões <strong>não-decrescentes</strong>: aplicar uma release
/// anterior à última já aplicada deixaria linhas órfãs de uma versão mais nova
/// (<c>versao_dataset</c> maior) sem serem marcadas. Essa regressão é barrada antes, no
/// disparo (guarda de versão progressiva em <c>GeoEtlOrquestrador.IniciarAsync</c>).
/// </remarks>
internal static class GeoStaleMarker
{
    // As 15 tabelas de reference data com proveniência (versao_dataset/vigente). É uma
    // whitelist CONSTANTE (nenhum dado de usuário entra no nome interpolado); os valores
    // (versão, instante) são parametrizados.
    private static readonly string[] Tabelas =
    [
        "pais",
        "estado",
        "estado_indicador",
        "estado_faixa_cep",
        "cidade",
        "cidade_indicador",
        "cidade_faixa_cep",
        "distrito",
        "distrito_faixa_cep",
        "bairro",
        "bairro_faixa_cep",
        "logradouro",
        "logradouro_complemento",
        "cep_grande_usuario",
        "cep_geral",
    ];

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "O nome da tabela vem de whitelist constante; versão, instante e início da carga são parametrizados (@versao/@agora/@inicio).")]
    public static async Task<int> MarcarStaleAsync(
        GeoDbContext contexto,
        string versaoAplicada,
        DateTimeOffset inicioCarga,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentException.ThrowIfNullOrWhiteSpace(versaoAplicada);

        int total = 0;
        foreach (string tabela in Tabelas)
        {
            // Parâmetros novos por iteração: um NpgsqlParameter não pode ser reusado em
            // múltiplos comandos.
            NpgsqlParameter pAgora = new("agora", agora);
            NpgsqlParameter pVersao = new("versao", versaoAplicada);
            NpgsqlParameter pInicio = new("inicio", inicioCarga);

            // Concatenação (não interpolação) para o nome da tabela — evita o EF1002, que
            // só dispara em strings interpoladas; os valores seguem parametrizados.
            string sql = "UPDATE " + tabela
                + " SET vigente = false, updated_at = @agora"
                + " WHERE vigente = true AND (versao_dataset < @versao"
                + " OR (versao_dataset = @versao AND coalesce(updated_at, created_at) < @inicio))";

            total += await contexto.Database.ExecuteSqlRawAsync(
                sql,
                [pAgora, pVersao, pInicio],
                cancellationToken).ConfigureAwait(false);
        }

        return total;
    }
}
