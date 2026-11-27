namespace Unifesspa.Geo.IntegrationTests.Registro;

using AwesomeAssertions;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.Abstractions.Interfaces;
using Unifesspa.Geo.Infrastructure;
using Unifesspa.Geo.Infrastructure.Cep;
using Unifesspa.Geo.Infrastructure.Core.Caching;
using Unifesspa.Geo.Infrastructure.Core.Persistence.Interceptors;
using Unifesspa.Geo.Infrastructure.Observability;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.Infrastructure.Persistence.Readers;

/// <summary>
/// Fixa a lista e a ordem dos serviços que a infraestrutura do Geo registra, sem subir o host:
/// quem registra por último vence e a ordem dos processos hospedados é a ordem de partida.
/// </summary>
public sealed class OrdemDoRegistroDosServicosTests
{
    private static readonly (Type Servico, ServiceLifetime Vida)[] ServicosDaInfraestrutura =
    [
        (typeof(AuditableInterceptor), ServiceLifetime.Scoped),
        (typeof(GeoDbContext), ServiceLifetime.Scoped),
        (typeof(IUnitOfWork), ServiceLifetime.Scoped),
        (typeof(IEstadoReader), ServiceLifetime.Scoped),
        (typeof(ICidadeReader), ServiceLifetime.Scoped),
        (typeof(IDistritoReader), ServiceLifetime.Scoped),
        (typeof(IBairroReader), ServiceLifetime.Scoped),
        (typeof(ILogradouroReader), ServiceLifetime.Scoped),
        (typeof(IGeoProximidadeReader), ServiceLifetime.Scoped),
        (typeof(ICepReader), ServiceLifetime.Scoped),
        (typeof(ICepResolver), ServiceLifetime.Scoped),
        (typeof(Lazy<ICacheService>), ServiceLifetime.Scoped),
        (typeof(IMemoryCache), ServiceLifetime.Singleton),
        (typeof(IGeoImportador), ServiceLifetime.Scoped),
        (typeof(GeoImportadorDistritoBairro), ServiceLifetime.Scoped),
        (typeof(ImportadorDeLogradourosPorCopia), ServiceLifetime.Scoped),
        (typeof(IGeoImportadorLocalidades), ServiceLifetime.Scoped),
        (typeof(TravaConsultivaDaCargaDne), ServiceLifetime.Singleton),
        (typeof(ImportadorDaVersaoDne), ServiceLifetime.Scoped),
        (typeof(FinalizacaoDaCargaDne), ServiceLifetime.Scoped),
        (typeof(EncerramentoDasCargasAbandonadas), ServiceLifetime.Scoped),
        (typeof(IGeoImportacaoService), ServiceLifetime.Scoped),
        (typeof(IGeoImportacaoExecutor), ServiceLifetime.Scoped),
        (typeof(IGeoImportacaoFila), ServiceLifetime.Singleton),
        (typeof(ArquivoDoPacoteDne), ServiceLifetime.Singleton),
        (typeof(IFonteDaExecucaoDne), ServiceLifetime.Singleton),
        (typeof(IGeoCepCacheInvalidador), ServiceLifetime.Scoped),
        (typeof(Lazy<IGeoCepCacheInvalidador>), ServiceLifetime.Scoped),
        (typeof(GeoEtlMetrics), ServiceLifetime.Singleton),
    ];

    [Fact(DisplayName = "A infraestrutura registra os serviços do Geo na ordem e com o tempo de vida de hoje")]
    public void Infraestrutura_RegistraNaOrdemEComOTempoDeVida()
    {
        ServiceCollection servicos = new();

        servicos.AddGeoInfrastructure();

        DoGeo(servicos).Should().Equal(ServicosDaInfraestrutura);
    }

    [Fact(DisplayName = "Os validadores das opções da carga ficam na ordem pacote, trava e prazos")]
    public void Validadores_FicamNaOrdemPacoteTravaEPrazos()
    {
        ServiceCollection servicos = ComGatilhos();

        IEnumerable<Type?> validadores = servicos
            .Where(d => d.ServiceType == typeof(IValidateOptions<EtlOpcoes>))
            .Select(d => d.ImplementationType);

        validadores.Should().Equal(typeof(ValidadorDasOpcoesDoPacote), typeof(ValidadorDaTravaDaCarga), typeof(ValidadorDosPrazosDaCarga));
    }

    [Fact(DisplayName = "Os processos hospedados ficam na ordem reconciliação na partida e depois processo em segundo plano")]
    public void ProcessosHospedados_FicamNaOrdemDaPartida()
    {
        ServiceCollection servicos = ComGatilhos();

        IEnumerable<Type?> processos = servicos.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType);

        processos.Should().Equal(typeof(GeoReconciliacaoStartupHostedService), typeof(GeoImportacaoBackgroundService));
    }

    [Fact(DisplayName = "Com o processo em segundo plano desligado nenhum processo hospedado é registrado")]
    public void ProcessoDesligado_NaoRegistraProcessoHospedado()
    {
        ServiceCollection servicos = ComGatilhos(("Geo:Etl:ProcessoEmSegundoPlanoHabilitado", "false"));

        servicos.Where(d => d.ServiceType == typeof(IHostedService)).Should().BeEmpty();
    }

    [Fact(DisplayName = "As opções da carga são lidas da seção Geo:Etl")]
    public void Opcoes_SaoLidasDaSecaoGeoEtl()
    {
        using ServiceProvider provedor = ComGatilhos(("Geo:Etl:DuracaoMaximaDaCarga", "02:00:00")).BuildServiceProvider();

        EtlOpcoes opcoes = provedor.GetRequiredService<IOptions<EtlOpcoes>>().Value;

        opcoes.DuracaoMaximaDaCarga.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact(DisplayName = "As opções da carga inválidas são recusadas na conferência da partida")]
    public void OpcoesInvalidas_SaoRecusadasNaPartida()
    {
        using ServiceProvider provedor = ComGatilhos(("Geo:Etl:TamanhoMaximoDoPacote", "0")).BuildServiceProvider();

        Action conferir = () => provedor.GetRequiredService<IStartupValidator>().Validate();

        conferir.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("Geo:Etl:TamanhoMaximoDoPacote");
    }

    private static (Type Servico, ServiceLifetime Vida)[] DoGeo(IServiceCollection servicos) =>
        [.. servicos.Where(EhDoGeo).Select(d => (d.ServiceType, d.Lifetime))];

    private static bool EhDoGeo(ServiceDescriptor descritor) =>
        descritor.ServiceType.Namespace!.StartsWith("Unifesspa.Geo", StringComparison.Ordinal)
        || descritor.ServiceType == typeof(IMemoryCache)
        || descritor.ServiceType == typeof(Lazy<ICacheService>)
        || descritor.ServiceType == typeof(Lazy<IGeoCepCacheInvalidador>);

    private static ServiceCollection ComGatilhos(params (string Chave, string Valor)[] opcoes)
    {
        IConfiguration configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(opcoes.Select(o => new KeyValuePair<string, string?>(o.Chave, o.Valor)))
            .Build();
        ServiceCollection servicos = new();
        servicos.AddGeoEtlGatilhos(configuracao);
        return servicos;
    }
}
