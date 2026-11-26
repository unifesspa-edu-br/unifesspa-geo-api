namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.NomesOficiais;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;
using Unifesspa.Geo.Kernel.Results;

using static Unifesspa.Geo.Infrastructure.Persistence.Etl.GeoEtlUpsert;

/// <summary>
/// Importa Cidade (com o recorte territorial embutido), indicadores da Cidade e faixas
/// de CEP da Cidade (ADR-0092) dentro da transação aberta por
/// <see cref="GeoImportadorPaisEstadoCidade"/>. Upsert por chave natural (código IBGE):
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
        Dictionary<string, CidadeTerritorioCru> territorios = await AgregarPorChaveAsync(
            fonte.LerCidadeTerritoriosAsync(cancellationToken), t => ChaveCodigo(t.CodigoIbge)).ConfigureAwait(false);

        Dictionary<string, Guid> cidadesPorCodigo = await ImportarCidadesAsync(
            fonte, estadosPorUf, territorios, relatorio.Tabela("cidade"), cancellationToken).ConfigureAwait(false);
        await ImportarIndicadoresAsync(fonte, cidadesPorCodigo, relatorio.Tabela("cidade_indicador"), cancellationToken).ConfigureAwait(false);
        await ImportarFaixasAsync(fonte, cidadesPorCodigo, relatorio.Tabela("cidade_faixa"), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, Guid>> ImportarCidadesAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> estadosPorUf,
        Dictionary<string, CidadeTerritorioCru> territorios,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Cidade> existentes =
            await _contexto.Cidades.ToDictionaryAsync(c => c.CodigoIbge, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        Dictionary<string, Guid> cidadesPorCodigo =
            await UpsertarCidadesAsync(fonte, estadosPorUf, territorios, existentes, contador, cancellationToken).ConfigureAwait(false);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
        return cidadesPorCodigo;
    }

    private async Task<Dictionary<string, Guid>> UpsertarCidadesAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> estadosPorUf,
        Dictionary<string, CidadeTerritorioCru> territorios,
        Dictionary<string, Cidade> existentes,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Guid> cidadesPorCodigo = new(StringComparer.Ordinal);
        Dictionary<string, Cidade> jaVistas = new(StringComparer.Ordinal);

        await foreach (CidadeCru cru in fonte.LerCidadesAsync(cancellationToken).ConfigureAwait(false))
        {
            contador.ContarLido();
            Cidade? cidade = UpsertarCidade(cru, estadosPorUf, territorios, fonte.Versao, existentes, jaVistas, contador);
            if (cidade is not null)
            {
                cidadesPorCodigo[cidade.CodigoIbge] = cidade.Id;
            }
        }

        return cidadesPorCodigo;
    }

    private Cidade? UpsertarCidade(
        CidadeCru cru,
        Dictionary<string, Guid> estadosPorUf,
        Dictionary<string, CidadeTerritorioCru> territorios,
        string versao,
        Dictionary<string, Cidade> existentes,
        Dictionary<string, Cidade> jaVistas,
        ContadorTabela contador)
    {
        if (ResolverChave(cru, estadosPorUf, contador) is not { } chave)
        {
            return null;
        }

        territorios.TryGetValue(chave.CodigoIbge, out CidadeTerritorioCru? territorio);
        string nome = NomeDaCidade(chave.CodigoIbge, cru, contador);
        CidadeParaCarga carga = new(chave, nome, cru, CoordenadaDne.De(cru.Latitude, cru.Longitude), territorio, versao);
        return Upsert(chave.CodigoIbge, jaVistas, existentes, carga.Criar, carga.Atualizar, _contexto.Cidades, contador);
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

        return new ChaveDaCidade(codigoIbge, uf, estadoId);
    }

    private async Task ImportarIndicadoresAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> cidadesPorCodigo,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        Dictionary<string, CidadeIndicadorCru> indicadores = await AgregarContandoAsync(
            fonte.LerCidadeIndicadoresAsync(cancellationToken), i => ChaveCodigo(i.CodigoIbge), contador).ConfigureAwait(false);
        Dictionary<Guid, CidadeIndicador> existentes =
            await _contexto.CidadeIndicadores.ToDictionaryAsync(i => i.CidadeId, cancellationToken).ConfigureAwait(false);
        UpsertarIndicadores(cidadesPorCodigo, indicadores, fonte.Versao, existentes, contador);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
    }

    // Lidos, sem chave e duplicados já foram contados na agregação; aqui só resta
    // resolver o órfão e fazer o upsert.
    private void UpsertarIndicadores(
        Dictionary<string, Guid> cidadesPorCodigo,
        Dictionary<string, CidadeIndicadorCru> indicadores,
        string versao,
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

            UpsertarIndicador(cidadeId, cru, versao, existentes, contador);
        }
    }

    private void UpsertarIndicador(
        Guid cidadeId,
        CidadeIndicadorCru cru,
        string versao,
        Dictionary<Guid, CidadeIndicador> existentes,
        ContadorTabela contador)
    {
        MetricasDaCidade m = MetricasDaCidade.De(cru, contador);

        if (existentes.TryGetValue(cidadeId, out CidadeIndicador? existente))
        {
            AtualizarIndicador(existente, cru, m, versao, contador);
        }
        else
        {
            CriarIndicador(cidadeId, cru, m, versao, contador);
        }
    }

    private void AtualizarIndicador(CidadeIndicador existente, CidadeIndicadorCru cru, MetricasDaCidade m, string versao, ContadorTabela contador)
    {
        existente.Atualizar(cru.Gentilico, cru.Prefeito, m.Area, m.Populacao, m.Densidade, m.Escolarizacao, m.Idh, m.Mortalidade, m.Receitas, m.Despesas, m.Pib, cru.Aniversario, versao);
        Tocar(_contexto, existente);
        contador.ContarAtualizado();
    }

    private void CriarIndicador(Guid cidadeId, CidadeIndicadorCru cru, MetricasDaCidade m, string versao, ContadorTabela contador)
    {
        Result<CidadeIndicador> criado = CidadeIndicador.Importar(cidadeId, cru.Gentilico, cru.Prefeito, m.Area, m.Populacao, m.Densidade, m.Escolarizacao, m.Idh, m.Mortalidade, m.Receitas, m.Despesas, m.Pib, cru.Aniversario, versao);
        if (criado.IsFailure)
        {
            contador.ContarIgnoradoSemChave();
            return;
        }

        _contexto.CidadeIndicadores.Add(criado.Value!);
        contador.ContarInserido();
    }

    private async Task ImportarFaixasAsync(
        IGeoFonteDados fonte,
        Dictionary<string, Guid> cidadesPorCodigo,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        // A fonte de faixa só expõe o id interno da cidade na DNE, não o código IBGE: a
        // chave estrangeira se resolve contra as cidades já carregadas, sem nova consulta
        // ao banco. O upsert é por chave natural (cidade, CEP inicial, CEP final), que
        // preserva o Id da faixa e não estoura a restrição de unicidade.
        Dictionary<int, Guid> cidadesPorIdDne = await ResolverIdsDneParaGuidAsync(
            fonte.LerCidadeIdsAsync(cancellationToken), cru => cru.IdCidade, cru => cru.CodigoIbge, cidadesPorCodigo).ConfigureAwait(false);
        Guid[] cidadeIds = [.. cidadesPorIdDne.Values];
        Dictionary<string, CidadeFaixaCep> existentes =
            (await _contexto.CidadeFaixasCep.Where(f => cidadeIds.Contains(f.CidadeId)).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(f => ChaveFaixa(f.CidadeId, f.CepInicial, f.CepFinal), StringComparer.Ordinal);
        await UpsertarFaixasAsync(fonte, cidadesPorIdDne, existentes, contador, cancellationToken).ConfigureAwait(false);
        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        GeoEtlLog.TabelaConcluida(_logger, contador);
    }

    private async Task UpsertarFaixasAsync(
        IGeoFonteDados fonte,
        Dictionary<int, Guid> cidadesPorIdDne,
        Dictionary<string, CidadeFaixaCep> existentes,
        ContadorTabela contador,
        CancellationToken cancellationToken)
    {
        Dictionary<string, CidadeFaixaCep> jaVistas = new(StringComparer.Ordinal);

        await foreach (CidadeFaixaCru cru in fonte.LerCidadeFaixasAsync(cancellationToken).ConfigureAwait(false))
        {
            contador.ContarLido();
            if (ResolverCidade(cru.IdCidade, cidadesPorIdDne, contador) is { } cidadeId)
            {
                UpsertarFaixa(cru, cidadeId, fonte.Versao, existentes, jaVistas, contador);
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

    private void UpsertarFaixa(
        CidadeFaixaCru cru,
        Guid cidadeId,
        string versao,
        Dictionary<string, CidadeFaixaCep> existentes,
        Dictionary<string, CidadeFaixaCep> jaVistas,
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
            ChaveFaixa(cidadeId, cepInicial, cepFinal), jaVistas, existentes,
            () => CidadeFaixaCep.Importar(cidadeId, cepInicial, cepFinal, versao),
            existente => existente.Atualizar(versao),
            _contexto.CidadeFaixasCep, contador);
    }

    private readonly record struct ChaveDaCidade(string CodigoIbge, string Uf, Guid EstadoId);

    private readonly record struct CidadeParaCarga(
        ChaveDaCidade Chave,
        string Nome,
        CidadeCru Cru,
        CoordenadaDne Coordenada,
        CidadeTerritorioCru? Territorio,
        string Versao)
    {
        // Busca e ordenação usam o nome normalizado (sem acento, caixa nem apóstrofo), derivado
        // do nome gravado; nome em branco fica sem normalizado e o domínio recusa a cidade.
        private string? NomeNormalizado =>
            string.IsNullOrWhiteSpace(Nome) ? null : BuscaTextualNormalizada.NormalizarNomeDoMunicipio(Nome);

        public Result<Cidade> Criar() =>
            Cidade.Importar(
                Chave.EstadoId, Chave.Uf, Chave.CodigoIbge, Nome, NomeNormalizado, Cru.Ddd,
                Coordenada.Latitude, Coordenada.Longitude, Coordenada.Ponto,
                Territorio?.MesorregiaoCodigo, Territorio?.MesorregiaoNome, Territorio?.MicrorregiaoCodigo, Territorio?.MicrorregiaoNome,
                Territorio?.RegiaoIntermediariaCodigo, Territorio?.RegiaoIntermediariaNome, Territorio?.RegiaoImediataCodigo, Territorio?.RegiaoImediataNome,
                Versao);

        public Result Atualizar(Cidade existente) =>
            existente.Atualizar(
                Chave.EstadoId, Chave.Uf, Nome, NomeNormalizado, Cru.Ddd,
                Coordenada.Latitude, Coordenada.Longitude, Coordenada.Ponto,
                Territorio?.MesorregiaoCodigo, Territorio?.MesorregiaoNome, Territorio?.MicrorregiaoCodigo, Territorio?.MicrorregiaoNome,
                Territorio?.RegiaoIntermediariaCodigo, Territorio?.RegiaoIntermediariaNome, Territorio?.RegiaoImediataCodigo, Territorio?.RegiaoImediataNome,
                Versao);
    }

    private readonly record struct MetricasDaCidade
    {
        public decimal? Area { get; init; }

        public int? Populacao { get; init; }

        public decimal? Densidade { get; init; }

        public decimal? Escolarizacao { get; init; }

        public decimal? Idh { get; init; }

        public decimal? Mortalidade { get; init; }

        public decimal? Receitas { get; init; }

        public decimal? Despesas { get; init; }

        public decimal? Pib { get; init; }

        public static MetricasDaCidade De(CidadeIndicadorCru cru, ContadorTabela contador) => new()
        {
            Area = MetricaIbge.Decimal(cru.AreaTerritorialKm2, "area_territorial_km2", contador),
            Populacao = MetricaIbge.Inteira(cru.PopulacaoResidente, "populacao_residente", contador),
            Densidade = MetricaIbge.Decimal(cru.DensidadeDemografica, "densidade_demografica", contador),
            Escolarizacao = MetricaIbge.Decimal(cru.Escolarizacao6a14, "escolarizacao_6_a_14_anos", contador),
            Idh = MetricaIbge.Decimal(cru.Idh, "idice_de_desenv_humano", contador),
            Mortalidade = MetricaIbge.Decimal(cru.MortalidadeInfantil, "mortalidade_infantil", contador),
            Receitas = MetricaIbge.Decimal(cru.Receitas, "receitas_realizadas", contador),
            Despesas = MetricaIbge.Decimal(cru.Despesas, "despesas_empenhadas", contador),
            Pib = MetricaIbge.Decimal(cru.PibPerCapita, "pib_per_capita", contador),
        };
    }
}
