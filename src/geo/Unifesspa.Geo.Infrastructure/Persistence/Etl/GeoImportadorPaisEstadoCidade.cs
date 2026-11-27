namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.NomesOficiais;

using static Unifesspa.Geo.Infrastructure.Persistence.Etl.GeoEtlUpsert;

/// <summary>
/// Importa o topo da hierarquia do Geo — País (só Brasil), Estado (+indicador
/// +faixas) e Cidade (+territorial embutido, +indicador, +faixas) — a partir do
/// dataset DNE (ADR-0092), executando dentro da transação ativa do <see cref="GeoDbContext"/>.
/// A gravação por chave natural (<c>Importar</c>/<c>Atualizar</c> do domínio) torna a recarga
/// idempotente.
/// </summary>
internal sealed class GeoImportadorPaisEstadoCidade : IGeoImportador
{
    private readonly GeoDbContext _contexto;
    private readonly ILogger<GeoImportadorPaisEstadoCidade> _logger;
    private readonly GeoImportadorEstados _estados;
    private readonly GeoImportadorCidades _cidades;

    public GeoImportadorPaisEstadoCidade(GeoDbContext contexto, ILogger<GeoImportadorPaisEstadoCidade> logger)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(logger);

        _contexto = contexto;
        _logger = logger;
        _estados = new GeoImportadorEstados(contexto, logger);
        _cidades = new GeoImportadorCidades(contexto, logger, NomesOficiaisDosMunicipios.Padrao);
    }

    public async Task<RelatorioImportacao> ImportarAsync(IGeoFonteDados fonte, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fonte);

        RelatorioImportacao relatorio = new(fonte.Versao);
        GeoEtlLog.LogCargaIniciada(_logger, fonte.Versao);
        await ImportarHierarquiaAsync(fonte, relatorio, cancellationToken).ConfigureAwait(false);
        GeoEtlLog.CargaConcluida(_logger, relatorio);
        return relatorio;
    }

    private async Task ImportarHierarquiaAsync(IGeoFonteDados fonte, RelatorioImportacao relatorio, CancellationToken cancellationToken)
    {
        Guid brasilId = await ImportarPaisAsync(fonte, relatorio.Tabela("pais"), cancellationToken).ConfigureAwait(false);
        if (brasilId == Guid.Empty)
        {
            // País de referência (Brasil) ausente no dataset: sem ele estados/cidades ficariam
            // órfãos e a carga retornaria verde-vazia, indistinguível de uma base
            // legitimamente vazia. Sinaliza explicitamente o caso.
            GeoEtlLog.LogBrasilAusente(_logger, fonte.Versao);
        }

        Dictionary<string, Guid> estadosPorUf =
            await _estados.ImportarAsync(fonte, brasilId, relatorio, cancellationToken).ConfigureAwait(false);
        await _cidades.ImportarAsync(fonte, estadosPorUf, relatorio, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid> ImportarPaisAsync(IGeoFonteDados fonte, ContadorTabela contador, CancellationToken cancellationToken)
    {
        Dictionary<string, Pais> existentes =
            await _contexto.Paises.ToDictionaryAsync(p => p.SiglaIso, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        ProvenienciaDaCarga proveniencia = new(fonte.Versao);
        Guid brasilId = await UpsertarBrasilAsync(fonte, proveniencia, existentes, contador, cancellationToken).ConfigureAwait(false);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
        return brasilId;
    }

    private async Task<Guid> UpsertarBrasilAsync(
        IGeoFonteDados fonte,
        ProvenienciaDaCarga proveniencia,
        Dictionary<string, Pais> existentes,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Pais> jaVistos = new(StringComparer.Ordinal);
        Guid brasilId = Guid.Empty;

        await foreach (PaisCru cru in fonte.LerPaisesAsync(cancellationToken).ConfigureAwait(false))
        {
            contador.ContarLido();
            if (ChaveDoBrasil(cru, contador) is { } siglaIso
                && UpsertarBrasil(siglaIso, cru, proveniencia, existentes, jaVistos, contador) is { } pais)
            {
                brasilId = pais.Id;
            }
        }

        return brasilId;
    }

    private static string? ChaveDoBrasil(PaisCru cru, ContadorTabela contador)
    {
        if (ChaveSigla(cru.SiglaIso) is not { } siglaIso)
        {
            contador.ContarIgnoradoSemChave();
            return null;
        }

        // Carga restrita ao Brasil (decisão TL do Epic) — os demais países não
        // são erro, apenas fora de escopo; não contam como ignorados.
        return string.Equals(siglaIso, "BRA", StringComparison.Ordinal) ? siglaIso : null;
    }

    private Pais? UpsertarBrasil(
        string siglaIso,
        PaisCru cru,
        ProvenienciaDaCarga proveniencia,
        Dictionary<string, Pais> existentes,
        Dictionary<string, Pais> jaVistos,
        ContadorTabela contador)
    {
        CodigosDoPaisNosCadastrosFederais codigos = new(cru.CodigoBcb, cru.CodigoRfb, cru.CodigoSped, cru.CodigoSiscomex);
        string sigla = cru.Sigla ?? string.Empty;
        string nome = cru.Nome ?? string.Empty;

        return Upsert(
            siglaIso, jaVistos, existentes,
            () => Pais.Importar(siglaIso, sigla, nome, codigos, proveniencia),
            existente => existente.Atualizar(sigla, nome, codigos, proveniencia),
            _contexto.Paises, contador);
    }
}
