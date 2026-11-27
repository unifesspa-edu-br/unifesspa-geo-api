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
        await ImportarIndicadoresAsync(estadosPorUf, indicadores, proveniencia, contadorIndicadores, cancellationToken).ConfigureAwait(false);
        await ImportarFaixasAsync(fonte, estadosPorUf, proveniencia, relatorio.Tabela("estado_faixa"), cancellationToken).ConfigureAwait(false);
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
        // Sem o País (Brasil) carregado, os estados ficariam sem vínculo com o país: aborta o nível.
        if (brasilId == Guid.Empty)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        GravacaoPorChaveNatural<Estado> gravacao = await GravacaoPorChaveNatural<Estado>.AbrirAsync(
            _contexto.Estados, e => e.Uf, contador, cancellationToken).ConfigureAwait(false);
        Dictionary<string, Guid> estadosPorUf = await GravarEstadosAsync(
            fonte, brasilId, indicadores, proveniencia, gravacao, cancellationToken).ConfigureAwait(false);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
        return estadosPorUf;
    }

    private static async Task<Dictionary<string, Guid>> GravarEstadosAsync(
        IGeoFonteDados fonte,
        Guid brasilId,
        Dictionary<string, EstadoIndicadorCru> indicadores,
        ProvenienciaDaCarga proveniencia,
        GravacaoPorChaveNatural<Estado> gravacao,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Guid> estadosPorUf = new(StringComparer.Ordinal);

        await foreach (EstadoCru cru in fonte.LerEstadosAsync(cancellationToken).ConfigureAwait(false))
        {
            gravacao.Contador.ContarLido();
            if (GravarEstado(cru, brasilId, indicadores, proveniencia, gravacao) is { } estado)
            {
                estadosPorUf[estado.Uf] = estado.Id;
            }
        }

        return estadosPorUf;
    }

    private static Estado? GravarEstado(
        EstadoCru cru,
        Guid brasilId,
        Dictionary<string, EstadoIndicadorCru> indicadores,
        ProvenienciaDaCarga proveniencia,
        GravacaoPorChaveNatural<Estado> gravacao)
    {
        if (ChaveSigla(cru.Uf) is not { } uf)
        {
            gravacao.Contador.ContarIgnoradoSemChave();
            return null;
        }

        ClassificacaoDaUfNoIbge classificacao = new(indicadores.GetValueOrDefault(uf)?.CodigoIbge, cru.Regiao);
        return gravacao.Gravar(
            uf,
            () => Estado.Importar(new UfNoPais(brasilId, uf), NomeDe(cru), classificacao, cru.Capital, PosicaoDe(cru), FaixaGeralDe(cru), proveniencia),
            existente => existente.Atualizar(brasilId, NomeDe(cru), classificacao, cru.Capital, PosicaoDe(cru), FaixaGeralDe(cru), proveniencia));
    }

    private static NomeComFormaDeBusca NomeDe(EstadoCru cru) => new(cru.Nome ?? string.Empty, cru.NomeSemAcento);

    private static PosicaoGeografica PosicaoDe(EstadoCru cru) => CoordenadaDne.Ler(cru.Latitude, cru.Longitude);

    private static FaixaDeCep FaixaGeralDe(EstadoCru cru) => new(cru.FaixaIni, cru.FaixaFim);

    private async Task ImportarIndicadoresAsync(
        Dictionary<string, Guid> estadosPorUf,
        Dictionary<string, EstadoIndicadorCru> indicadores,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, EstadoIndicador> existentes =
            await _contexto.EstadoIndicadores.ToDictionaryAsync(i => i.EstadoId, cancellationToken).ConfigureAwait(false);
        GravarIndicadores(estadosPorUf, indicadores, proveniencia, existentes, contador);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
    }

    // Lidos, sem chave e duplicados já foram contados na agregação; aqui só resta
    // resolver o órfão (UF sem estado carregado) e gravar pela chave natural.
    private void GravarIndicadores(
        Dictionary<string, Guid> estadosPorUf,
        Dictionary<string, EstadoIndicadorCru> indicadores,
        ProvenienciaDaCarga proveniencia,
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

            GravarIndicador(estadoId, cru, proveniencia, existentes, contador);
        }
    }

    private void GravarIndicador(
        Guid estadoId,
        EstadoIndicadorCru cru,
        ProvenienciaDaCarga proveniencia,
        Dictionary<Guid, EstadoIndicador> existentes,
        ContadorTabela contador)
    {
        MetricasDoEstado metricas = MetricasDe(cru, contador);

        if (existentes.TryGetValue(estadoId, out EstadoIndicador? existente))
        {
            AtualizarIndicador(existente, cru, metricas, proveniencia, contador);
        }
        else
        {
            CriarIndicador(estadoId, cru, metricas, proveniencia, contador);
        }
    }

    private void AtualizarIndicador(
        EstadoIndicador existente,
        EstadoIndicadorCru cru,
        MetricasDoEstado metricas,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador)
    {
        existente.Atualizar(cru.Gentilico, cru.Governador, metricas, proveniencia);
        Tocar(_contexto, existente);
        contador.ContarAtualizado();
    }

    private void CriarIndicador(
        Guid estadoId,
        EstadoIndicadorCru cru,
        MetricasDoEstado metricas,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador)
    {
        Result<EstadoIndicador> criado = EstadoIndicador.Importar(estadoId, cru.Gentilico, cru.Governador, metricas, proveniencia);
        if (criado.IsFailure)
        {
            contador.ContarIgnoradoSemChave();
            return;
        }

        _contexto.EstadoIndicadores.Add(criado.Value!);
        contador.ContarInserido();
    }

    private static MetricasDoEstado MetricasDe(EstadoIndicadorCru cru, ContadorTabela contador) => new()
    {
        AreaKm2 = MetricaIbge.Decimal(cru.AreaTerritorialKm2, "area_territorial_km2", contador),
        PopulacaoResidente2022 = MetricaIbge.Inteira(cru.PopulacaoResidente2022, "populacao_residente_2022", contador),
        DensidadeDemografica = MetricaIbge.Decimal(cru.DensidadeDemografica, "densidade_demografica_hab_km2", contador),
        MatriculasEnsinoFundamental2023 = MetricaIbge.Inteira(cru.MatriculasEnsinoFundamental2023, "matriculas_ensino_fundamental_2023", contador),
        Idh = MetricaIbge.Decimal(cru.Idh, "idh_indice_desenv_humano", contador),
        ReceitasBrutas = MetricaIbge.Decimal(cru.ReceitasBrutas, "total_receitas_brutas_realizadas", contador),
        DespesasBrutas = MetricaIbge.Decimal(cru.DespesasBrutas, "total_despesas_brutas_empenhadas", contador),
        RendimentoMensalPerCapita = MetricaIbge.Inteira(cru.RendimentoMensalPerCapita, "rendimento_mensal_per_capita", contador),
        TotalVeiculos2023 = MetricaIbge.Inteira(cru.TotalVeiculos2023, "total_veiculos_2023", contador),
    };

    private async Task ImportarFaixasAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> estadosPorUf,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        // Gravação por chave natural (estado, CEP inicial, CEP final), em vez de apagar e
        // inserir: preserva o Id da faixa entre cargas, e a repetição na própria fonte
        // não estoura a restrição de unicidade. Faixa que some numa versão nova é
        // tratada pela política de vigência da atualização periódica.
        Guid[] estadoIds = [.. estadosPorUf.Values];
        Dictionary<string, EstadoFaixaCep> existentes =
            (await _contexto.EstadoFaixasCep.Where(f => estadoIds.Contains(f.EstadoId)).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(f => ChaveFaixa(f.EstadoId, f.CepInicial, f.CepFinal), StringComparer.Ordinal);
        GravacaoPorChaveNatural<EstadoFaixaCep> gravacao = GravacaoPorChaveNatural<EstadoFaixaCep>.SobreRegistrosCarregados(
            _contexto.EstadoFaixasCep, existentes, contador);
        await GravarFaixasAsync(fonte, estadosPorUf, proveniencia, gravacao, cancellationToken).ConfigureAwait(false);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
    }

    private static async Task GravarFaixasAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> estadosPorUf,
        ProvenienciaDaCarga proveniencia,
        GravacaoPorChaveNatural<EstadoFaixaCep> gravacao,
        CancellationToken cancellationToken)
    {
        await foreach (EstadoFaixaCru cru in fonte.LerEstadoFaixasAsync(cancellationToken).ConfigureAwait(false))
        {
            gravacao.Contador.ContarLido();
            if (ResolverEstado(cru.Uf, estadosPorUf, gravacao.Contador) is { } estadoId)
            {
                GravarFaixa(cru, estadoId, proveniencia, gravacao);
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

    private static void GravarFaixa(
        EstadoFaixaCru cru,
        Guid estadoId,
        ProvenienciaDaCarga proveniencia,
        GravacaoPorChaveNatural<EstadoFaixaCep> gravacao)
    {
        FaixaDeCep faixa = new(cru.FaixaIni, cru.FaixaFim);
        if (!faixa.TemCepInicial || !faixa.TemCepFinal)
        {
            gravacao.Contador.ContarIgnoradoSemChave();
            return;
        }

        gravacao.Gravar(
            ChaveFaixa(estadoId, faixa.CepInicial!, faixa.CepFinal!),
            () => EstadoFaixaCep.Importar(estadoId, faixa, cru.Descricao, proveniencia),
            existente => existente.Atualizar(cru.Descricao, proveniencia));
    }
}
