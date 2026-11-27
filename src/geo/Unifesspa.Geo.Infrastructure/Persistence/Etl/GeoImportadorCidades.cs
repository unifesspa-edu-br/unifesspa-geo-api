namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.NomesOficiais;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;
using Unifesspa.Geo.Kernel.Results;

using static Unifesspa.Geo.Infrastructure.Persistence.Etl.RegrasDaCargaPorChaveNatural;

/// <summary>
/// Importa Cidade (com o recorte territorial embutido), indicadores da Cidade e faixas
/// de CEP da Cidade (ADR-0092) dentro da transação aberta por
/// <see cref="GeoImportadorPaisEstadoCidade"/>. Gravação por chave natural (código IBGE):
/// a recarga preserva o Guid e não duplica. O nome gravado é o oficial do IBGE, por
/// código; só quando o código não consta na lista oficial vale o nome da DNE.
/// </summary>
internal sealed class GeoImportadorCidades
{
    private readonly GeoDbContext _contexto;
    private readonly ILogger _logger;
    private readonly NomesOficiaisDosMunicipios _nomesOficiais;

    public GeoImportadorCidades(GeoDbContext contexto, ILogger logger, NomesOficiaisDosMunicipios nomesOficiais)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(nomesOficiais);

        _contexto = contexto;
        _logger = logger;
        _nomesOficiais = nomesOficiais;
    }

    public async Task ImportarAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> estadosPorUf,
        RelatorioImportacao relatorio,
        CancellationToken cancellationToken)
    {
        ProvenienciaDaCarga proveniencia = new(fonte.Versao);
        Dictionary<string, CidadeTerritorioCru> territorios = await AgregarPorChaveAsync(
            fonte.LerCidadeTerritoriosAsync(cancellationToken), t => ChaveCodigo(t.CodigoIbge)).ConfigureAwait(false);

        Dictionary<string, Guid> cidadesPorCodigo = await ImportarCidadesAsync(
            fonte, estadosPorUf, territorios, proveniencia, relatorio.Tabela("cidade"), cancellationToken).ConfigureAwait(false);
        await ImportarIndicadoresAsync(fonte, cidadesPorCodigo, proveniencia, relatorio.Tabela("cidade_indicador"), cancellationToken).ConfigureAwait(false);
        await ImportarFaixasAsync(fonte, cidadesPorCodigo, proveniencia, relatorio.Tabela("cidade_faixa"), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, Guid>> ImportarCidadesAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> estadosPorUf,
        Dictionary<string, CidadeTerritorioCru> territorios,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        GravacaoPorChaveNatural<Cidade> gravacao = await GravacaoPorChaveNatural<Cidade>.AbrirAsync(
            _contexto.Cidades, c => c.CodigoIbge, contador, cancellationToken).ConfigureAwait(false);
        Dictionary<string, Guid> cidadesPorCodigo =
            await GravarCidadesAsync(fonte, estadosPorUf, territorios, proveniencia, gravacao, cancellationToken).ConfigureAwait(false);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
        return cidadesPorCodigo;
    }

    private async Task<Dictionary<string, Guid>> GravarCidadesAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> estadosPorUf,
        Dictionary<string, CidadeTerritorioCru> territorios,
        ProvenienciaDaCarga proveniencia,
        GravacaoPorChaveNatural<Cidade> gravacao,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Guid> cidadesPorCodigo = new(StringComparer.Ordinal);

        await foreach (CidadeCru cru in fonte.LerCidadesAsync(cancellationToken).ConfigureAwait(false))
        {
            gravacao.Contador.ContarLido();
            if (GravarCidade(cru, estadosPorUf, territorios, proveniencia, gravacao) is { } cidade)
            {
                cidadesPorCodigo[cidade.CodigoIbge] = cidade.Id;
            }
        }

        return cidadesPorCodigo;
    }

    private Cidade? GravarCidade(
        CidadeCru cru,
        Dictionary<string, Guid> estadosPorUf,
        Dictionary<string, CidadeTerritorioCru> territorios,
        ProvenienciaDaCarga proveniencia,
        GravacaoPorChaveNatural<Cidade> gravacao)
    {
        ContadorTabela contador = gravacao.Contador;
        if (ResolverChave(cru, estadosPorUf, contador) is not { } chave)
        {
            return null;
        }

        territorios.TryGetValue(chave.CodigoIbge, out CidadeTerritorioCru? territorio);
        string nome = NomeDaCidade(chave.CodigoIbge, cru, contador);
        CidadeParaCarga carga = new(chave, NomeComBusca(nome), cru.Ddd, CoordenadaDne.Ler(cru.Latitude, cru.Longitude), DivisaoDe(territorio), proveniencia);
        return gravacao.Gravar(chave.CodigoIbge, carga.Criar, carga.Atualizar);
    }

    // O nome oficial do IBGE prevalece sobre o da DNE. Código fora da lista oficial não
    // aborta a carga: mantém o nome da DNE e fica registrado no relatório.
    private string NomeDaCidade(string codigoIbge, CidadeCru cru, ContadorTabela contador)
    {
        if (_nomesOficiais.TentarObter(codigoIbge, out string? nomeOficial))
        {
            return nomeOficial;
        }

        contador.ContarParseDegradado($"nome_oficial_ibge_ausente:{codigoIbge}");
        return cru.Nome ?? string.Empty;
    }

    private static ChaveDaCidade? ResolverChave(CidadeCru cru, Dictionary<string, Guid> estadosPorUf, ContadorTabela contador)
    {
        if (ChaveCodigo(cru.CodigoIbge) is not { } codigoIbge)
        {
            contador.ContarIgnoradoSemChave();
            return null;
        }

        if (ChaveSigla(cru.Uf) is not { } uf || !estadosPorUf.TryGetValue(uf, out Guid estadoId))
        {
            contador.ContarOrfao(codigoIbge);
            return null;
        }

        return new ChaveDaCidade(codigoIbge, new EstadoDaCidade(estadoId, uf));
    }

    // Busca e ordenação usam o nome normalizado (sem acento, caixa nem apóstrofo), derivado do
    // nome gravado; nome em branco fica sem normalizado e o domínio recusa a cidade.
    private static NomeComFormaDeBusca NomeComBusca(string nome) =>
        new(nome, string.IsNullOrWhiteSpace(nome) ? null : BuscaTextualNormalizada.NormalizarNomeDoMunicipio(nome));

    private static DivisaoRegionalDaCidade DivisaoDe(CidadeTerritorioCru? territorio) =>
        new(
            new RegiaoGeografica(territorio?.MesorregiaoCodigo, territorio?.MesorregiaoNome),
            new RegiaoGeografica(territorio?.MicrorregiaoCodigo, territorio?.MicrorregiaoNome),
            new RegiaoGeografica(territorio?.RegiaoIntermediariaCodigo, territorio?.RegiaoIntermediariaNome),
            new RegiaoGeografica(territorio?.RegiaoImediataCodigo, territorio?.RegiaoImediataNome));

    private async Task ImportarIndicadoresAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> cidadesPorCodigo,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        Dictionary<string, CidadeIndicadorCru> indicadores = await AgregarContandoAsync(
            fonte.LerCidadeIndicadoresAsync(cancellationToken), i => ChaveCodigo(i.CodigoIbge), contador).ConfigureAwait(false);
        Dictionary<Guid, CidadeIndicador> existentes =
            await _contexto.CidadeIndicadores.ToDictionaryAsync(i => i.CidadeId, cancellationToken).ConfigureAwait(false);
        GravarIndicadores(cidadesPorCodigo, indicadores, proveniencia, existentes, contador);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
    }

    // Lidos, sem chave e duplicados já foram contados na agregação; aqui só resta
    // resolver o órfão e gravar pela chave natural.
    private void GravarIndicadores(
        Dictionary<string, Guid> cidadesPorCodigo,
        Dictionary<string, CidadeIndicadorCru> indicadores,
        ProvenienciaDaCarga proveniencia,
        Dictionary<Guid, CidadeIndicador> existentes,
        ContadorTabela contador)
    {
        foreach ((string codigo, CidadeIndicadorCru cru) in indicadores)
        {
            if (!cidadesPorCodigo.TryGetValue(codigo, out Guid cidadeId))
            {
                contador.ContarOrfao(codigo);
                continue;
            }

            GravarIndicador(cidadeId, cru, proveniencia, existentes, contador);
        }
    }

    private void GravarIndicador(
        Guid cidadeId,
        CidadeIndicadorCru cru,
        ProvenienciaDaCarga proveniencia,
        Dictionary<Guid, CidadeIndicador> existentes,
        ContadorTabela contador)
    {
        MetricasDaCidade metricas = MetricasDe(cru, contador);

        if (existentes.TryGetValue(cidadeId, out CidadeIndicador? existente))
        {
            AtualizarIndicador(existente, cru, metricas, proveniencia, contador);
        }
        else
        {
            CriarIndicador(cidadeId, cru, metricas, proveniencia, contador);
        }
    }

    private void AtualizarIndicador(
        CidadeIndicador existente,
        CidadeIndicadorCru cru,
        MetricasDaCidade metricas,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador)
    {
        existente.Atualizar(cru.Gentilico, cru.Prefeito, cru.Aniversario, metricas, proveniencia);
        MarcarComoRevista(_contexto, existente);
        contador.ContarAtualizado();
    }

    private void CriarIndicador(
        Guid cidadeId,
        CidadeIndicadorCru cru,
        MetricasDaCidade metricas,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador)
    {
        Result<CidadeIndicador> criado = CidadeIndicador.Importar(cidadeId, cru.Gentilico, cru.Prefeito, cru.Aniversario, metricas, proveniencia);
        if (criado.IsFailure)
        {
            contador.ContarIgnoradoSemChave();
            return;
        }

        _contexto.CidadeIndicadores.Add(criado.Value!);
        contador.ContarInserido();
    }

    private static MetricasDaCidade MetricasDe(CidadeIndicadorCru cru, ContadorTabela contador) => new()
    {
        AreaKm2 = MetricaIbge.Decimal(cru.AreaTerritorialKm2, "area_territorial_km2", contador),
        PopulacaoResidente = MetricaIbge.Inteira(cru.PopulacaoResidente, "populacao_residente", contador),
        DensidadeDemografica = MetricaIbge.Decimal(cru.DensidadeDemografica, "densidade_demografica", contador),
        Escolarizacao6a14 = MetricaIbge.Decimal(cru.Escolarizacao6a14, "escolarizacao_6_a_14_anos", contador),
        Idh = MetricaIbge.Decimal(cru.Idh, "idice_de_desenv_humano", contador),
        MortalidadeInfantil = MetricaIbge.Decimal(cru.MortalidadeInfantil, "mortalidade_infantil", contador),
        Receitas = MetricaIbge.Decimal(cru.Receitas, "receitas_realizadas", contador),
        Despesas = MetricaIbge.Decimal(cru.Despesas, "despesas_empenhadas", contador),
        PibPerCapita = MetricaIbge.Decimal(cru.PibPerCapita, "pib_per_capita", contador),
    };

    private async Task ImportarFaixasAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> cidadesPorCodigo,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        // A fonte de faixa só expõe o id interno da cidade na DNE, não o código IBGE: a
        // chave estrangeira se resolve contra as cidades já carregadas, sem nova consulta
        // ao banco. A gravação é por chave natural (cidade, CEP inicial, CEP final), que
        // preserva o Id da faixa e não estoura a restrição de unicidade.
        Dictionary<int, Guid> cidadesPorIdDne = await ResolverIdsDneParaGuidAsync(
            fonte.LerCidadeIdsAsync(cancellationToken), cru => cru.IdCidade, cru => cru.CodigoIbge, cidadesPorCodigo).ConfigureAwait(false);
        GravacaoPorChaveNatural<CidadeFaixaCep> gravacao =
            await AbrirGravacaoDasFaixasAsync(cidadesPorIdDne, contador, cancellationToken).ConfigureAwait(false);
        await GravarFaixasAsync(fonte, cidadesPorIdDne, proveniencia, gravacao, cancellationToken).ConfigureAwait(false);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
    }

    // Carrega só as faixas das cidades desta carga, não a tabela inteira.
    private async Task<GravacaoPorChaveNatural<CidadeFaixaCep>> AbrirGravacaoDasFaixasAsync(
        Dictionary<int, Guid> cidadesPorIdDne,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        Guid[] cidadeIds = [.. cidadesPorIdDne.Values];
        Dictionary<string, CidadeFaixaCep> existentes =
            (await _contexto.CidadeFaixasCep.Where(f => cidadeIds.Contains(f.CidadeId)).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(f => ChaveFaixa(f.CidadeId, f.CepInicial, f.CepFinal), StringComparer.Ordinal);
        return GravacaoPorChaveNatural<CidadeFaixaCep>.SobreRegistrosCarregados(_contexto.CidadeFaixasCep, existentes, contador);
    }

    private static async Task GravarFaixasAsync(
        IGeoFonteDados fonte,
        Dictionary<int, Guid> cidadesPorIdDne,
        ProvenienciaDaCarga proveniencia,
        GravacaoPorChaveNatural<CidadeFaixaCep> gravacao,
        CancellationToken cancellationToken)
    {
        await foreach (CidadeFaixaCru cru in fonte.LerCidadeFaixasAsync(cancellationToken).ConfigureAwait(false))
        {
            gravacao.Contador.ContarLido();
            if (ResolverCidade(cru.IdCidade, cidadesPorIdDne, gravacao.Contador) is { } cidadeId)
            {
                GravarFaixa(cru, cidadeId, proveniencia, gravacao);
            }
        }
    }

    private static Guid? ResolverCidade(int? idCidade, Dictionary<int, Guid> cidadesPorIdDne, ContadorTabela contador)
    {
        if (idCidade is int id && cidadesPorIdDne.TryGetValue(id, out Guid cidadeId))
        {
            return cidadeId;
        }

        contador.ContarOrfao(idCidade?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(sem id)");
        return null;
    }

    private static void GravarFaixa(
        CidadeFaixaCru cru,
        Guid cidadeId,
        ProvenienciaDaCarga proveniencia,
        GravacaoPorChaveNatural<CidadeFaixaCep> gravacao)
    {
        FaixaDeCep faixa = new(cru.FaixaIni, cru.FaixaFim);
        if (!faixa.TemCepInicial || !faixa.TemCepFinal)
        {
            gravacao.Contador.ContarIgnoradoSemChave();
            return;
        }

        gravacao.Gravar(
            ChaveFaixa(cidadeId, faixa.CepInicial!, faixa.CepFinal!),
            () => CidadeFaixaCep.Importar(cidadeId, faixa, proveniencia),
            existente => existente.Atualizar(proveniencia));
    }

    private readonly record struct ChaveDaCidade(string CodigoIbge, EstadoDaCidade Estado);

    private readonly record struct CidadeParaCarga(
        ChaveDaCidade Chave,
        NomeComFormaDeBusca Nome,
        string? Ddd,
        PosicaoGeografica Posicao,
        DivisaoRegionalDaCidade Divisao,
        ProvenienciaDaCarga Proveniencia)
    {
        public Result<Cidade> Criar() =>
            Cidade.Importar(Chave.CodigoIbge, Chave.Estado, Nome, Ddd, Posicao, Divisao, Proveniencia);

        public Result Atualizar(Cidade existente) =>
            existente.Atualizar(Chave.Estado, Nome, Ddd, Posicao, Divisao, Proveniencia);
    }
}
