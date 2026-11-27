namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Fonte do dataset DNE para o ETL (ADR-0092). Abstrai a origem dos registros crus
/// por tabela — em produção a implementação <see cref="DneEsquemaDeCargaFonte"/> lê via
/// <c>SELECT</c> streamado do esquema de carga; em teste, uma fonte em memória.
/// A <see cref="Versao"/> (AAAAMM) é a fonte única de verdade da release: o importador
/// a usa para gravar a proveniência, evitando carimbar dados de uma versão com outra.
/// </summary>
/// <remarks>
/// Cobre toda a hierarquia DNE: o topo (País/Estado/Cidade) e as folhas
/// (Distrito/Bairro/Logradouro e satélites). As folhas trazem os ids int4
/// da fonte para resolução de FK.
/// </remarks>
internal interface IGeoFonteDados
{
    /// <summary>Release DNE (AAAAMM) que esta fonte representa.</summary>
    string Versao { get; }

    IAsyncEnumerable<PaisCru> LerPaisesAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<EstadoCru> LerEstadosAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<EstadoIndicadorCru> LerEstadoIndicadoresAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<EstadoFaixaCru> LerEstadoFaixasAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<CidadeCru> LerCidadesAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<CidadeTerritorioCru> LerCidadeTerritoriosAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<CidadeIndicadorCru> LerCidadeIndicadoresAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<CidadeFaixaCru> LerCidadeFaixasAsync(CancellationToken cancellationToken);

    // --- Folhas ---

    /// <summary>Mapa <c>id_cidade</c> → <c>cidade_ibge</c> para resolver a FK <c>cidade_id</c> (int4) em Guid.</summary>
    IAsyncEnumerable<CidadeIdCru> LerCidadeIdsAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<DistritoCru> LerDistritosAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<FaixaLocalidadeCru> LerDistritoFaixasAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<BairroCru> LerBairrosAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<FaixaLocalidadeCru> LerBairroFaixasAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<CepGrandeUsuarioCru> LerCepGrandesUsuariosAsync(CancellationToken cancellationToken);

    /// <summary>CEP geral de cidade/distrito: linhas da fonte de logradouro sem nome (tipo e nomes vazios).</summary>
    IAsyncEnumerable<CepGeralCru> LerCepGeraisAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<LogradouroComplementoCru> LerLogradouroComplementosAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<LogradouroCru> LerLogradourosAsync(CancellationToken cancellationToken);
}
