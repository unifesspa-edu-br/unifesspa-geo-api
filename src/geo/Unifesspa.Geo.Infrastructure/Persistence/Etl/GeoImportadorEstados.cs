namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;
using Unifesspa.Geo.Kernel.Results;

using static Unifesspa.Geo.Infrastructure.Persistence.Etl.GeoEtlUpsert;

/// <summary>
/// Importa Estado, indicadores do Estado e faixas de CEP do Estado (ADR-0092) dentro da
/// transação aberta por <see cref="GeoImportadorPaisEstadoCidade"/>. Gravação por chave
/// natural (UF): a recarga preserva o Guid e não duplica.
/// </summary>
internal sealed class GeoImportadorEstados
{
    private readonly GeoDbContext _contexto;
    private readonly ILogger _logger;

    public GeoImportadorEstados(GeoDbContext contexto, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(logger);

        _contexto = contexto;
        _logger = logger;
    }

    public async Task<Dictionary<string, Guid>> ImportarAsync(
        IGeoFonteDados fonte,
        Guid brasilId,
        RelatorioImportacao relatorio,
        CancellationToken cancellationToken)
    {
        // Os indicadores contam lidos, sem chave e duplicados no próprio contador da
        // tabela: o relatório reflete o total da fonte, não só os registros com chave.
        ContadorTabela contadorIndicadores = relatorio.Tabela("estado_indicador");
        Dictionary<string, EstadoIndicadorCru> indicadores = await AgregarContandoAsync(
            fonte.LerEstadoIndicadoresAsync(cancellationToken), i => ChaveSigla(i.Uf), contadorIndicadores).ConfigureAwait(false);

        ProvenienciaDaCarga proveniencia = new(fonte.Versao);
        Dictionary<string, Guid> estadosPorUf = await ImportarEstadosAsync(
            fonte, brasilId, indicadores, proveniencia, relatorio.Tabela("estado"), cancellationToken).ConfigureAwait(false);
        await ImportarIndicadoresAsync(estadosPorUf, indicadores, fonte.Versao, contadorIndicadores, cancellationToken).ConfigureAwait(false);
        await ImportarFaixasAsync(fonte, estadosPorUf, relatorio.Tabela("estado_faixa"), cancellationToken).ConfigureAwait(false);
        return estadosPorUf;
    }

    private async Task<Dictionary<string, Guid>> ImportarEstadosAsync(
        IGeoFonteDados fonte,
        Guid brasilId,
        Dictionary<string, EstadoIndicadorCru> indicadores,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        // Sem o País (Brasil) carregado, os estados ficariam órfãos de FK — aborta o nível.
        if (brasilId == Guid.Empty)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        Dictionary<string, Estado> existentes =
            await _contexto.Estados.ToDictionaryAsync(e => e.Uf, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        Dictionary<string, Guid> estadosPorUf = await UpsertarEstadosAsync(
            fonte, brasilId, indicadores, proveniencia, existentes, contador, cancellationToken).ConfigureAwait(false);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
        return estadosPorUf;
    }

    private async Task<Dictionary<string, Guid>> UpsertarEstadosAsync(
        IGeoFonteDados fonte,
        Guid brasilId,
        Dictionary<string, EstadoIndicadorCru> indicadores,
        ProvenienciaDaCarga proveniencia,
        Dictionary<string, Estado> existentes,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Guid> estadosPorUf = new(StringComparer.Ordinal);
        Dictionary<string, Estado> jaVistos = new(StringComparer.Ordinal);

        await foreach (EstadoCru cru in fonte.LerEstadosAsync(cancellationToken).ConfigureAwait(false))
        {
            contador.ContarLido();
            if (UpsertarEstado(cru, brasilId, indicadores, proveniencia, existentes, jaVistos, contador) is { } estado)
            {
                estadosPorUf[estado.Uf] = estado.Id;
            }
        }

        return estadosPorUf;
    }

    private Estado? UpsertarEstado(
        EstadoCru cru,
        Guid brasilId,
        Dictionary<string, EstadoIndicadorCru> indicadores,
        ProvenienciaDaCarga proveniencia,
        Dictionary<string, Estado> existentes,
        Dictionary<string, Estado> jaVistos,
        ContadorTabela contador)
    {
        if (ChaveSigla(cru.Uf) is not { } uf)
        {
            contador.ContarIgnoradoSemChave();
            return null;
        }

        ClassificacaoDaUfNoIbge classificacao = new(indicadores.GetValueOrDefault(uf)?.CodigoIbge, cru.Regiao);
        return Upsert(
            uf, jaVistos, existentes,
            () => Estado.Importar(new UfNoPais(brasilId, uf), NomeDe(cru), classificacao, cru.Capital, PosicaoDe(cru), FaixaGeralDe(cru), proveniencia),
            existente => existente.Atualizar(brasilId, NomeDe(cru), classificacao, cru.Capital, PosicaoDe(cru), FaixaGeralDe(cru), proveniencia),
            _contexto.Estados, contador);
    }

    private static NomeComFormaDeBusca NomeDe(EstadoCru cru) => new(cru.Nome ?? string.Empty, cru.NomeSemAcento);

    private static PosicaoGeografica PosicaoDe(EstadoCru cru) => CoordenadaDne.De(cru.Latitude, cru.Longitude);

    private static FaixaDeCep FaixaGeralDe(EstadoCru cru) => new(cru.FaixaIni, cru.FaixaFim);

    private async Task ImportarIndicadoresAsync(
        Dictionary<string, Guid> estadosPorUf,
        Dictionary<string, EstadoIndicadorCru> indicadores,
        string versao,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, EstadoIndicador> existentes =
            await _contexto.EstadoIndicadores.ToDictionaryAsync(i => i.EstadoId, cancellationToken).ConfigureAwait(false);
        UpsertarIndicadores(estadosPorUf, indicadores, versao, existentes, contador);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
    }

    // Lidos, sem chave e duplicados já foram contados na agregação; aqui só resta
    // resolver o órfão (UF sem estado carregado) e fazer o upsert.
    private void UpsertarIndicadores(
        Dictionary<string, Guid> estadosPorUf,
        Dictionary<string, EstadoIndicadorCru> indicadores,
        string versao,
        Dictionary<Guid, EstadoIndicador> existentes,
        ContadorTabela contador)
    {
        foreach ((string uf, EstadoIndicadorCru cru) in indicadores)
        {
            if (!estadosPorUf.TryGetValue(uf, out Guid estadoId))
            {
                contador.ContarOrfao(uf);
                continue;
            }

            UpsertarIndicador(estadoId, cru, versao, existentes, contador);
        }
    }

    private void UpsertarIndicador(
        Guid estadoId,
        EstadoIndicadorCru cru,
        string versao,
        Dictionary<Guid, EstadoIndicador> existentes,
        ContadorTabela contador)
    {
        MetricasDoEstado m = MetricasDoEstado.De(cru, contador);

        if (existentes.TryGetValue(estadoId, out EstadoIndicador? existente))
        {
            AtualizarIndicador(existente, cru, m, versao, contador);
        }
        else
        {
            CriarIndicador(estadoId, cru, m, versao, contador);
        }
    }

    private void AtualizarIndicador(EstadoIndicador existente, EstadoIndicadorCru cru, MetricasDoEstado m, string versao, ContadorTabela contador)
    {
        existente.Atualizar(cru.Gentilico, cru.Governador, m.Area, m.Populacao, m.Densidade, m.Matriculas, m.Idh, m.Receitas, m.Despesas, m.Rendimento, m.Veiculos, versao);
        Tocar(_contexto, existente);
        contador.ContarAtualizado();
    }

    private void CriarIndicador(Guid estadoId, EstadoIndicadorCru cru, MetricasDoEstado m, string versao, ContadorTabela contador)
    {
        Result<EstadoIndicador> criado = EstadoIndicador.Importar(estadoId, cru.Gentilico, cru.Governador, m.Area, m.Populacao, m.Densidade, m.Matriculas, m.Idh, m.Receitas, m.Despesas, m.Rendimento, m.Veiculos, versao);
        if (criado.IsFailure)
        {
            contador.ContarIgnoradoSemChave();
            return;
        }

        _contexto.EstadoIndicadores.Add(criado.Value!);
        contador.ContarInserido();
    }

    private async Task ImportarFaixasAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> estadosPorUf,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        // Upsert por chave natural (estado, CEP inicial, CEP final), em vez de apagar e
        // inserir: preserva o Id da faixa entre cargas, e a repetição na própria fonte
        // não estoura a restrição de unicidade. Faixa que some numa versão nova é
        // tratada pela política de vigência da atualização periódica.
        Guid[] estadoIds = [.. estadosPorUf.Values];
        Dictionary<string, EstadoFaixaCep> existentes =
            (await _contexto.EstadoFaixasCep.Where(f => estadoIds.Contains(f.EstadoId)).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(f => ChaveFaixa(f.EstadoId, f.CepInicial, f.CepFinal), StringComparer.Ordinal);
        await UpsertarFaixasAsync(fonte, estadosPorUf, existentes, contador, cancellationToken).ConfigureAwait(false);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
    }

    private async Task UpsertarFaixasAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> estadosPorUf,
        Dictionary<string, EstadoFaixaCep> existentes,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        Dictionary<string, EstadoFaixaCep> jaVistas = new(StringComparer.Ordinal);

        await foreach (EstadoFaixaCru cru in fonte.LerEstadoFaixasAsync(cancellationToken).ConfigureAwait(false))
        {
            contador.ContarLido();
            if (ResolverEstado(cru.Uf, estadosPorUf, contador) is { } estadoId)
            {
                UpsertarFaixa(cru, estadoId, fonte.Versao, existentes, jaVistas, contador);
            }
        }
    }

    private static Guid? ResolverEstado(string? ufCru, Dictionary<string, Guid> estadosPorUf, ContadorTabela contador)
    {
        string? uf = ChaveSigla(ufCru);
        if (uf is not null && estadosPorUf.TryGetValue(uf, out Guid estadoId))
        {
            return estadoId;
        }

        contador.ContarOrfao(uf ?? "(sem uf)");
        return null;
    }

    private void UpsertarFaixa(
        EstadoFaixaCru cru,
        Guid estadoId,
        string versao,
        Dictionary<string, EstadoFaixaCep> existentes,
        Dictionary<string, EstadoFaixaCep> jaVistas,
        ContadorTabela contador)
    {
        string? cepInicial = ChaveCodigo(cru.FaixaIni);
        string? cepFinal = ChaveCodigo(cru.FaixaFim);
        if (cepInicial is null || cepFinal is null)
        {
            contador.ContarIgnoradoSemChave();
            return;
        }

        Upsert(
            ChaveFaixa(estadoId, cepInicial, cepFinal), jaVistas, existentes,
            () => EstadoFaixaCep.Importar(estadoId, cepInicial, cepFinal, cru.Descricao, versao),
            existente => existente.Atualizar(cru.Descricao, versao),
            _contexto.EstadoFaixasCep, contador);
    }

    private readonly record struct MetricasDoEstado
    {
        public decimal? Area { get; init; }

        public int? Populacao { get; init; }

        public decimal? Densidade { get; init; }

        public int? Matriculas { get; init; }

        public decimal? Idh { get; init; }

        public decimal? Receitas { get; init; }

        public decimal? Despesas { get; init; }

        public int? Rendimento { get; init; }

        public int? Veiculos { get; init; }

        public static MetricasDoEstado De(EstadoIndicadorCru cru, ContadorTabela contador) => new()
        {
            Area = MetricaIbge.Decimal(cru.AreaTerritorialKm2, "area_territorial_km2", contador),
            Populacao = MetricaIbge.Inteira(cru.PopulacaoResidente2022, "populacao_residente_2022", contador),
            Densidade = MetricaIbge.Decimal(cru.DensidadeDemografica, "densidade_demografica_hab_km2", contador),
            Matriculas = MetricaIbge.Inteira(cru.MatriculasEnsinoFundamental2023, "matriculas_ensino_fundamental_2023", contador),
            Idh = MetricaIbge.Decimal(cru.Idh, "idh_indice_desenv_humano", contador),
            Receitas = MetricaIbge.Decimal(cru.ReceitasBrutas, "total_receitas_brutas_realizadas", contador),
            Despesas = MetricaIbge.Decimal(cru.DespesasBrutas, "total_despesas_brutas_empenhadas", contador),
            Rendimento = MetricaIbge.Inteira(cru.RendimentoMensalPerCapita, "rendimento_mensal_per_capita", contador),
            Veiculos = MetricaIbge.Inteira(cru.TotalVeiculos2023, "total_veiculos_2023", contador),
        };
    }
}
