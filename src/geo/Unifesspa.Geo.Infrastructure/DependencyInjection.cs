namespace Unifesspa.Geo.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.Abstractions.Interfaces;
using Unifesspa.Geo.Infrastructure.Caching;
using Unifesspa.Geo.Infrastructure.Cep;
using Unifesspa.Geo.Infrastructure.Core.Caching;
using Unifesspa.Geo.Infrastructure.Core.Persistence;
using Unifesspa.Geo.Infrastructure.Observability;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.Infrastructure.Persistence.Readers;

/// <summary>
/// Registra a infraestrutura do módulo Geo: DbContext, interceptadores transversais
/// (exclusão lógica e auditoria), leitores da referência, consulta do CEP e carga da DNE.
/// O <c>GeoDbContext</c> ativa o plugin NetTopologySuite do Npgsql via o hook do
/// <c>UseGeoNpgsqlConventions</c> (ADR-0091) — paridade com o design-time factory.
/// </summary>
public static class GeoInfrastructureRegistration
{
    internal const string ConnectionStringName = "GeoDb";

    public static IServiceCollection AddGeoInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPersistenciaDoGeo();
        services.AddLeitoresDaReferencia();
        services.AddConsultaDoCep();
        services.AddImportadoresDaCargaDne();
        services.AddExecucaoDaCargaDne();

        return services;
    }

    private static void AddPersistenciaDoGeo(this IServiceCollection services)
    {
        services.AddGeoEfInterceptors();

        services.AddDbContext<GeoDbContext>((serviceProvider, options) =>
            options.UseGeoNpgsqlConventions<GeoDbContext>(
                serviceProvider,
                ConnectionStringName,
                npgsql => npgsql.UseNetTopologySuite()));

        services.AddScoped<IUnitOfWork>(serviceProvider =>
            serviceProvider.GetRequiredService<GeoDbContext>());
    }

    private static void AddLeitoresDaReferencia(this IServiceCollection services)
    {
        // Leitores da API pública de dados de referência: listagem por cursor + detalhe
        // por chave natural. Só expõem o que está vigente (ADR-0092).
        services.AddScoped<IEstadoReader, EstadoReader>();
        services.AddScoped<ICidadeReader, CidadeReader>();
        services.AddScoped<IDistritoReader, DistritoReader>();
        services.AddScoped<IBairroReader, BairroReader>();
        services.AddScoped<ILogradouroReader, LogradouroReader>();

        // Proximidade geoespacial: filtro ST_DWithin (índice GIST) + ordenação ST_Distance
        // sobre geography (ADR-0091). Só dados de referência vigentes.
        services.AddScoped<IGeoProximidadeReader, GeoProximidadeReader>();
    }

    private static void AddConsultaDoCep(this IServiceCollection services)
    {
        // Consulta de CEP: leitor da cascata (logradouro → grande usuário → faixa) +
        // resolvedor com cache por selo de versão (ADR-0090/0092). O Lazy<ICacheService>
        // difere a conexão com o Redis para o resolvedor degradar ao banco quando o cache
        // está fora (espelha o Lazy da carga).
        services.AddScoped<ICepReader, CepReader>();
        services.AddScoped<ICepResolver, CepResolver>();
        services.AddOptions<GeoCepCacheOptions>();
        services.AddOptions<GeoCepLookupOptions>();
        services.AddScoped(sp => new Lazy<ICacheService>(sp.GetRequiredService<ICacheService>));
        // Memoização em processo do selo de versão vigente do cache de CEP: 1 ida ao Redis
        // no caminho quente em vez de 2. AddMemoryCache é idempotente.
        services.AddMemoryCache();
    }

    private static void AddImportadoresDaCargaDne(this IServiceCollection services)
    {
        // Carga DNE (ADR-0092) — serviços de carga dos dados de referência.
        // Topo da hierarquia (País/Estado/Cidade):
        services.AddScoped<IGeoImportador, GeoImportadorPaisEstadoCidade>();

        // Folhas (Distrito/Bairro/Logradouro + satélites): compõe a gravação por chave natural
        // de Distrito/Bairro e a cópia em lote dos logradouros na transação única.
        services.AddScoped<GeoImportadorDistritoBairro>();
        services.AddScoped<ImportadorDeLogradourosPorCopia>();
        services.AddScoped<IGeoImportadorLocalidades, GeoImportadorLocalidades>();
    }

    private static void AddExecucaoDaCargaDne(this IServiceCollection services)
    {
        // Atualização periódica: registro e consulta (API), executor da carga (processo em segundo plano),
        // trava consultiva, fila em processo, fonte da execução, selo do cache e métricas.
        services.AddSingleton<TravaConsultivaDaCargaDne>();
        services.AddScoped<ImportadorDaVersaoDne>();
        services.AddScoped<FinalizacaoDaCargaDne>();
        services.AddScoped<EncerramentoDasCargasAbandonadas>();
        services.AddScoped<IGeoImportacaoService, RegistroDasImportacoes>();
        services.AddScoped<IGeoImportacaoExecutor, CargaDaImportacao>();
        services.AddSingleton<IGeoImportacaoFila, GeoImportacaoFila>();
        services.AddPacoteDaCargaDne();
        services.AddInvalidacaoDoCacheDeCep();
        services.AddSingleton<GeoEtlMetrics>();
    }

    private static void AddPacoteDaCargaDne(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            EtlOpcoes opcoes = sp.GetRequiredService<IOptions<EtlOpcoes>>().Value;
            return new ArquivoDoPacoteDne(
                opcoes.DiretorioDoPacote,
                opcoes.QuantidadeMaximaDeEntradasDoPacote,
                opcoes.TamanhoMaximoDoPacote);
        });
        services.AddSingleton<IFonteDaExecucaoDne, EsquemaDeCargaDne>();
    }

    private static void AddInvalidacaoDoCacheDeCep(this IServiceCollection services)
    {
        services.AddScoped<IGeoCepCacheInvalidador, RedisGeoCepCacheInvalidador>();
        // Lazy para o executor: difere a resolução da cadeia Redis (IConnectionMultiplexer
        // conecta na construção) até o momento de selar — só no processo em segundo plano, nunca no disparo.
        services.AddScoped(sp => new Lazy<IGeoCepCacheInvalidador>(sp.GetRequiredService<IGeoCepCacheInvalidador>));
    }

    /// <summary>
    /// Registra os gatilhos hospedados da carga: a reconciliação na partida e o
    /// <c>BackgroundService</c> que executa as cargas enfileiradas. <strong>Deve ser chamado
    /// no Program APÓS <c>AddDbContextMigrationsOnStartup&lt;GeoDbContext&gt;()</c></strong> — como
    /// <c>HostOptions.ServicesStartConcurrently</c> é <see langword="false"/>, a ordem de
    /// registro é a ordem de start: migrations → reconciliação → processo em segundo plano.
    /// </summary>
    public static IServiceCollection AddGeoEtlGatilhos(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOpcoesDaCargaDne(configuration);

        EtlOpcoes opcoes = configuration.GetSection(EtlOpcoes.SectionName).Get<EtlOpcoes>() ?? new EtlOpcoes();

        if (opcoes.ProcessoEmSegundoPlanoHabilitado)
        {
            services.AddProcessosHospedadosDaCarga();
        }

        return services;
    }

    private static void AddOpcoesDaCargaDne(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<EtlOpcoes>, ValidadorDasOpcoesDoPacote>();
        services.AddSingleton<IValidateOptions<EtlOpcoes>, ValidadorDaTravaDaCarga>();
        services.AddSingleton<IValidateOptions<EtlOpcoes>, ValidadorDosPrazosDaCarga>();
        services.AddOptions<EtlOpcoes>().Bind(configuration.GetSection(EtlOpcoes.SectionName)).ValidateOnStart();
    }

    private static void AddProcessosHospedadosDaCarga(this IServiceCollection services)
    {
        // Reconciliação ANTES do processo em segundo plano: hosted service cujo StartAsync é
        // aguardado pelo host, limpando o diretório de pacotes, liberando o índice único parcial
        // e descartando os esquemas de carga órfãos antes que qualquer envio seja aceito.
        services.AddHostedService<GeoReconciliacaoStartupHostedService>();
        services.AddHostedService<GeoImportacaoBackgroundService>();
    }
}
