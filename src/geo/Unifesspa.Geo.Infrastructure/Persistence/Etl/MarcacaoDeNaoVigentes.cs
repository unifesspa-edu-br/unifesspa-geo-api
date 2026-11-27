namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.EntityFrameworkCore;

using Npgsql;

/// <summary>
/// Política de registros não vigentes da atualização periódica: ao aplicar uma versão,
/// as linhas dos dados de referência que não foram revistas na carga atual são marcadas
/// <c>vigente = false</c> dentro da transação única, preservando o histórico sem exclusão
/// física.
/// </summary>
internal static class MarcacaoDeNaoVigentes
{
    private static readonly string[] TabelasMarcadas =
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

    public static async Task<int> MarcarAsync(
        GeoDbContext contexto,
        string versaoAplicada,
        DateTimeOffset inicioCarga,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentException.ThrowIfNullOrWhiteSpace(versaoAplicada);

        int total = 0;
        foreach (string tabela in TabelasMarcadas)
        {
            total += await ExecutarComandoAsync(contexto, tabela, versaoAplicada, inicioCarga, agora, cancellationToken)
                .ConfigureAwait(false);
        }

        return total;
    }

    private static Task<int> ExecutarComandoAsync(
        GeoDbContext contexto,
        string tabela,
        string versaoAplicada,
        DateTimeOffset inicioCarga,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        NpgsqlParameter[] parametros =
        [
            new("agora", agora),
            new("versao", versaoAplicada),
            new("inicio", inicioCarga),
        ];

        return contexto.Database.ExecuteSqlRawAsync(ComandoDaTabela(tabela), parametros, cancellationToken);
    }

    private static string ComandoDaTabela(string tabela) =>
        $"UPDATE {tabela} SET vigente = false, updated_at = @agora WHERE vigente = true AND (versao_dataset < @versao OR (versao_dataset = @versao AND coalesce(updated_at, created_at) < @inicio))";
}
