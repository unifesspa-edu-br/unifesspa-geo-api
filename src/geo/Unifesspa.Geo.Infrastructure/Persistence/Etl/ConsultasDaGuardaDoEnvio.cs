namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Consultas que decidem o destino de um pacote recebido: a última versão aplicada (piso da guarda
/// de versão progressiva) e a última carga concluída (referência da repetição por resumo SHA-256).
/// </summary>
internal static class ConsultasDaGuardaDoEnvio
{
    /// <summary>
    /// Maior versão entre as cidades publicadas e as execuções concluídas. Execuções que falharam
    /// não entram: uma tentativa falha de versão futura não bloqueia versões intermediárias válidas.
    /// </summary>
    public static async Task<string?> ObterUltimaVersaoAplicadaAsync(this GeoDbContext contexto, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        string? versaoDados = await contexto.Cidades
            .MaxAsync(c => (string?)c.VersaoDataset, cancellationToken)
            .ConfigureAwait(false);
        string? versaoHistorico = await contexto.ImportacaoExecucoes
            .Where(e => e.Status == StatusImportacao.Concluida)
            .MaxAsync(e => (string?)e.VersaoDataset, cancellationToken)
            .ConfigureAwait(false);

        return MaiorVersao(versaoDados, versaoHistorico);
    }

    /// <summary>
    /// Identificador da última execução concluída (por instante de conclusão e, no empate, por
    /// identificador), quando o pacote dela tem o mesmo resumo SHA-256; senão, <see langword="null"/>.
    /// </summary>
    public static async Task<Guid?> ObterPublicacaoDoMesmoPacoteAsync(
        this GeoDbContext contexto,
        string sha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var ultima = await contexto.ImportacaoExecucoes
            .AsNoTracking()
            .Where(e => e.Status == StatusImportacao.Concluida)
            .OrderByDescending(e => e.ConcluidoEm)
            .ThenByDescending(e => e.Id)
            .Select(e => new { e.Id, e.PacoteSha256 })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return ultima is not null && ultima.PacoteSha256 == sha256 ? ultima.Id : null;
    }

    private static string? MaiorVersao(string? a, string? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        return string.CompareOrdinal(a, b) >= 0 ? a : b;
    }
}
