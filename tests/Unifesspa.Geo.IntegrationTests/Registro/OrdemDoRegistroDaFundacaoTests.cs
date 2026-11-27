namespace Unifesspa.Geo.IntegrationTests.Registro;

using AwesomeAssertions;

using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using StackExchange.Redis;

using Unifesspa.Geo.Infrastructure.Core.Caching;
using Unifesspa.Geo.Infrastructure.Core.Cors;
using Unifesspa.Geo.Infrastructure.Core.Cryptography;
using Unifesspa.Geo.Infrastructure.Core.DependencyInjection;
using Unifesspa.Geo.Infrastructure.Core.Errors;
using Unifesspa.Geo.Infrastructure.Core.Middleware;
using Unifesspa.Geo.Infrastructure.Core.Pagination;
using Unifesspa.Geo.IntegrationTests.Cifragem;

using FrameworkCorsOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;
using GeoCorsOptions = Unifesspa.Geo.Infrastructure.Core.Cors.CorsOptions;

/// <summary>
/// Fixa a sequência de serviços (tipo e tempo de vida) que cada registro da fundação acrescenta
/// à coleção, sem subir o host: a ordem decide quem vence e quando cada conferência da partida roda.
/// </summary>
public sealed class OrdemDoRegistroDaFundacaoTests
{
    private const string ConfiguracaoDaPartida =
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions]";

    private static readonly (string Tipo, ServiceLifetime Vida)[] OpcoesBase =
    [
        Registro(typeof(IOptions<>), ServiceLifetime.Singleton),
        Registro(typeof(IOptionsSnapshot<>), ServiceLifetime.Scoped),
        Registro(typeof(IOptionsMonitor<>), ServiceLifetime.Singleton),
        Registro(typeof(IOptionsFactory<>), ServiceLifetime.Transient),
        Registro(typeof(IOptionsMonitorCache<>), ServiceLifetime.Singleton),
    ];

    private static readonly (string Tipo, ServiceLifetime Vida)[] ConferenciaNaPartida =
    [
        Registro(typeof(IStartupValidator), ServiceLifetime.Transient),
        (ConfiguracaoDaPartida, ServiceLifetime.Transient),
    ];

    private static readonly (string Tipo, ServiceLifetime Vida)[] SequenciaDaCifragem =
    [
        .. OpcoesBase,
        Registro(typeof(IOptionsChangeTokenSource<EncryptionOptions>), ServiceLifetime.Singleton),
        Registro(typeof(IConfigureOptions<EncryptionOptions>), ServiceLifetime.Singleton),
        Registro(typeof(IValidateOptions<EncryptionOptions>), ServiceLifetime.Singleton),
        Registro(typeof(IValidateOptions<EncryptionOptions>), ServiceLifetime.Singleton),
        .. ConferenciaNaPartida,
        Registro(typeof(IGeoEncryptionService), ServiceLifetime.Singleton),
        Registro(typeof(IHostedService), ServiceLifetime.Singleton),
    ];

    private static readonly (string Tipo, ServiceLifetime Vida)[] SequenciaDoRegistroDaRequisicao =
    [
        .. OpcoesBase,
        Registro(typeof(IOptionsChangeTokenSource<RequestLoggingOptions>), ServiceLifetime.Singleton),
        Registro(typeof(IConfigureOptions<RequestLoggingOptions>), ServiceLifetime.Singleton),
        Registro(typeof(IPostConfigureOptions<RequestLoggingOptions>), ServiceLifetime.Singleton),
        Registro(typeof(IValidateOptions<RequestLoggingOptions>), ServiceLifetime.Singleton),
        .. ConferenciaNaPartida,
        Registro(typeof(QueryStringMasker), ServiceLifetime.Singleton),
    ];

    private static readonly (string Tipo, ServiceLifetime Vida)[] SequenciaDaPaginacao =
    [
        .. OpcoesBase,
        Registro(typeof(IOptionsChangeTokenSource<CursorPaginationOptions>), ServiceLifetime.Singleton),
        Registro(typeof(IConfigureOptions<CursorPaginationOptions>), ServiceLifetime.Singleton),
        .. Enumerable.Repeat(Registro(typeof(IValidateOptions<CursorPaginationOptions>), ServiceLifetime.Singleton), 4),
        .. ConferenciaNaPartida,
        Registro(typeof(CursorEncoder), ServiceLifetime.Singleton),
        Registro(typeof(TimeProvider), ServiceLifetime.Singleton),
        Registro(typeof(IDomainErrorRegistration), ServiceLifetime.Singleton),
        Registro(typeof(IPostConfigureOptions<ApiBehaviorOptions>), ServiceLifetime.Singleton),
    ];

    private static readonly (string Tipo, ServiceLifetime Vida)[] SequenciaDoCache =
    [
        .. OpcoesBase,
        Registro(typeof(IOptionsChangeTokenSource<RedisOptions>), ServiceLifetime.Singleton),
        Registro(typeof(IConfigureOptions<RedisOptions>), ServiceLifetime.Singleton),
        Registro(typeof(IValidateOptions<RedisOptions>), ServiceLifetime.Singleton),
        .. ConferenciaNaPartida,
        Registro(typeof(IConnectionMultiplexer), ServiceLifetime.Singleton),
        Registro(typeof(ICacheService), ServiceLifetime.Scoped),
    ];

    private static readonly (string Tipo, ServiceLifetime Vida)[] SequenciaDoCors =
    [
        .. OpcoesBase,
        Registro(typeof(IOptionsChangeTokenSource<GeoCorsOptions>), ServiceLifetime.Singleton),
        Registro(typeof(IConfigureOptions<GeoCorsOptions>), ServiceLifetime.Singleton),
        Registro(typeof(IValidateOptions<GeoCorsOptions>), ServiceLifetime.Singleton),
        .. ConferenciaNaPartida,
        Registro(typeof(ICorsService), ServiceLifetime.Transient),
        Registro(typeof(ICorsPolicyProvider), ServiceLifetime.Transient),
        Registro(typeof(IConfigureOptions<FrameworkCorsOptions>), ServiceLifetime.Transient),
    ];

    [Fact(DisplayName = "A cifragem registra opções, conferências, serviço de cifra e aquecimento nessa ordem")]
    public void Cifragem_RegistraNaOrdem()
    {
        ServiceCollection servicos = new();

        servicos.AddGeoEncryption(ConfiguracaoDaSecao("Geo:Encryption:Provider", "local"));

        Sequencia(servicos).Should().Equal(SequenciaDaCifragem);
    }

    [Fact(DisplayName = "A cifragem sem seção na configuração não registra a vinculação das opções")]
    public void Cifragem_SemSecao_NaoRegistraAVinculacao()
    {
        ServiceCollection servicos = new();

        servicos.AddGeoEncryption(ConfiguracaoDaSecao("Outra:Chave", "x"));

        Sequencia(servicos).Should().NotContain(Registro(typeof(IConfigureOptions<EncryptionOptions>), ServiceLifetime.Singleton));
    }

    [Fact(DisplayName = "O registro da requisição registra opções, ajuste final, conferência e mascaramento nessa ordem")]
    public void RegistroDaRequisicao_RegistraNaOrdem()
    {
        ServiceCollection servicos = new();

        servicos.AddRequestLogging(ConfiguracaoDaSecao("RequestLogging:ValorMascarado", "###"));

        Sequencia(servicos).Should().Equal(SequenciaDoRegistroDaRequisicao);
    }

    [Fact(DisplayName = "A paginação por cursor registra opções, quatro conferências, codificador, relógio, erros e ajuste do MVC")]
    public void PaginacaoPorCursor_RegistraNaOrdem()
    {
        ServiceCollection servicos = new();

        servicos.AddCursorPagination(ConfiguracaoDaSecao("Pagination:Nada", "x"));

        Sequencia(servicos).Should().Equal(SequenciaDaPaginacao);
    }

    [Fact(DisplayName = "O cache registra opções, conferência, conexão com o Redis e serviço de cache nessa ordem")]
    public void Cache_RegistraNaOrdem()
    {
        ServiceCollection servicos = new();

        servicos.AddGeoCache(ConfiguracaoDaSecao("Redis:Nada", "x"), new AmbienteDeTeste(Environments.Development));

        Sequencia(servicos).Should().Equal(SequenciaDoCache);
    }

    [Fact(DisplayName = "A política de CORS registra opções, conferência, serviços do CORS e ajuste da política nessa ordem")]
    public void Cors_RegistraNaOrdem()
    {
        ServiceCollection servicos = new();

        servicos.AddCorsConfiguration(ConfiguracaoDaSecao("Cors:Nada", "x"), new AmbienteDeTeste(Environments.Development));

        Sequencia(servicos).Should().Equal(SequenciaDoCors);
    }

    [Fact(DisplayName = "Sem Postgres nem Redis configurados nenhuma verificação de saúde é registrada")]
    public void Saude_SemConfiguracao_NaoRegistraVerificacao()
    {
        IReadOnlyList<HealthCheckRegistration> registros = VerificacoesDeSaude();

        registros.Should().BeEmpty();
    }

    [Fact(DisplayName = "Só com a cadeia de conexão do Postgres registra a verificação postgres com as etiquetas ready e db")]
    public void Saude_SoComPostgres_RegistraPostgres()
    {
        IReadOnlyList<HealthCheckRegistration> registros = VerificacoesDeSaude(("ConnectionStrings:GeoDb", "Host=exemplo.invalid"));

        registros.Select(r => r.Name).Should().Equal("postgres");
        registros[0].Tags.Should().BeEquivalentTo(["ready", "db"]);
    }

    [Fact(DisplayName = "Só com a cadeia de conexão do Redis registra a verificação redis com as etiquetas ready e cache")]
    public void Saude_SoComRedis_RegistraRedis()
    {
        IReadOnlyList<HealthCheckRegistration> registros = VerificacoesDeSaude(("Redis:ConnectionString", "exemplo.invalid:6379"));

        registros.Select(r => r.Name).Should().Equal("redis");
        registros[0].Tags.Should().BeEquivalentTo(["ready", "cache"]);
    }

    [Fact(DisplayName = "Com Postgres e Redis configurados registra postgres antes de redis")]
    public void Saude_ComOsDois_RegistraPostgresAntesDeRedis()
    {
        IReadOnlyList<HealthCheckRegistration> registros = VerificacoesDeSaude(
            ("ConnectionStrings:GeoDb", "Host=exemplo.invalid"),
            ("Redis:ConnectionString", "exemplo.invalid:6379"));

        registros.Select(r => r.Name).Should().Equal("postgres", "redis");
    }

    [Fact(DisplayName = "Cadeia de conexão do Postgres em branco não registra a verificação")]
    public void Saude_PostgresEmBranco_NaoRegistra()
    {
        IReadOnlyList<HealthCheckRegistration> registros = VerificacoesDeSaude(("ConnectionStrings:GeoDb", "   "));

        registros.Should().BeEmpty();
    }

    [Fact(DisplayName = "Nomes sensíveis da requisição: descarta vazios, apara, ignora a caixa e mantém a primeira ocorrência")]
    public void Normalizacao_NomesSensiveis_DescartaVaziosAparaEMantemAPrimeira()
    {
        RequestLoggingOptions opcoes = OpcoesDaRequisicao(configure: o =>
        {
            o.NomesParametrosSensiveis.Clear();
            foreach (string nome in new[] { "  Cpf ", "", "NIS", "cpf", "   ", "nis ", "Rg" })
            {
                o.NomesParametrosSensiveis.Add(nome);
            }
        });

        opcoes.NomesParametrosSensiveis.Should().Equal("Cpf", "NIS", "Rg");
    }

    [Fact(DisplayName = "Lista de prefixos silenciados vazia continua vazia depois da normalização")]
    public void Normalizacao_ListaVazia_ContinuaVazia()
    {
        RequestLoggingOptions opcoes = OpcoesDaRequisicao(configure: o => o.PrefixosSilenciados.Clear());

        opcoes.PrefixosSilenciados.Should().BeEmpty();
    }

    [Fact(DisplayName = "Os valores padrão das listas da requisição saem inalterados da normalização")]
    public void Normalizacao_Padroes_SaemInalterados()
    {
        RequestLoggingOptions opcoes = OpcoesDaRequisicao();

        opcoes.NomesParametrosSensiveis.Should().Equal("cpf", "email", "senha", "password", "token");
        opcoes.PrefixosSilenciados.Should().Equal("/health", "/metrics");
    }

    [Fact(DisplayName = "Opções da requisição: a seção acrescenta antes e o ajuste em código vem depois e vence")]
    public void OpcoesDaRequisicao_SecaoPrimeiroEDepoisAjusteEmCodigo()
    {
        IConfiguration configuracao = ConfiguracaoEmMemoria(
            ("RequestLogging:ValorMascarado", "###"),
            ("RequestLogging:PrefixosSilenciados:0", "/da-secao"));

        RequestLoggingOptions opcoes = OpcoesDaRequisicao(configuracao, o =>
        {
            o.ValorMascarado = "@@@";
            o.PrefixosSilenciados.Add("/do-codigo");
        });

        opcoes.ValorMascarado.Should().Be("@@@");
        opcoes.PrefixosSilenciados.Should().Equal("/health", "/metrics", "/da-secao", "/do-codigo");
    }

    [Fact(DisplayName = "Opções da requisição: sem ajuste em código vale o que a seção vinculou")]
    public void OpcoesDaRequisicao_SoSecao_ValeOQueFoiVinculado()
    {
        IConfiguration configuracao = ConfiguracaoEmMemoria(("RequestLogging:ValorMascarado", "###"));

        RequestLoggingOptions opcoes = OpcoesDaRequisicao(configuracao);

        opcoes.ValorMascarado.Should().Be("###");
    }

    [Fact(DisplayName = "Opções da requisição: sem a seção na configuração só o ajuste em código vale")]
    public void OpcoesDaRequisicao_SemSecao_SoOAjusteEmCodigo()
    {
        IConfiguration configuracao = ConfiguracaoEmMemoria(("Outra:Chave", "x"));

        RequestLoggingOptions opcoes = OpcoesDaRequisicao(configuracao, o => o.ValorMascarado = "@@@");

        opcoes.ValorMascarado.Should().Be("@@@");
    }

    [Fact(DisplayName = "Opções de cifragem: a seção vincula primeiro e o ajuste em código vem depois e vence só no que mexe")]
    public void OpcoesDeCifragem_SecaoPrimeiroEDepoisAjusteEmCodigo()
    {
        string chaveDaSecao = OpcoesDeCifragemParaTeste.ChaveEmBase64De(32);
        string chaveDoCodigo = Convert.ToBase64String([.. OpcoesDeCifragemParaTeste.ChaveDe(32).Reverse()]);
        IConfiguration configuracao = ConfiguracaoEmMemoria(
            ("Geo:Encryption:Provider", "local"),
            ("Geo:Encryption:LocalKey", chaveDaSecao),
            ("Geo:Encryption:VaultTransitMount", "montagem-da-secao"));

        EncryptionOptions opcoes = OpcoesDeCifragem(configuracao, o => o.LocalKey = chaveDoCodigo);

        opcoes.LocalKey.Should().Be(chaveDoCodigo);
        opcoes.VaultTransitMount.Should().Be("montagem-da-secao");
    }

    [Fact(DisplayName = "Opções de cifragem: sem ajuste em código vale a chave vinculada da seção")]
    public void OpcoesDeCifragem_SoSecao_ValeAChaveVinculada()
    {
        string chaveDaSecao = OpcoesDeCifragemParaTeste.ChaveEmBase64De(32);
        IConfiguration configuracao = ConfiguracaoEmMemoria(("Geo:Encryption:LocalKey", chaveDaSecao));

        EncryptionOptions opcoes = OpcoesDeCifragem(configuracao, configure: null);

        opcoes.LocalKey.Should().Be(chaveDaSecao);
    }

    [Fact(DisplayName = "Opções de cifragem: sem configuração nenhuma só o ajuste em código vale")]
    public void OpcoesDeCifragem_SemConfiguracao_SoOAjusteEmCodigo()
    {
        string chaveDoCodigo = OpcoesDeCifragemParaTeste.ChaveEmBase64De(32);

        EncryptionOptions opcoes = OpcoesDeCifragem(configuracao: null, o => o.LocalKey = chaveDoCodigo);

        opcoes.LocalKey.Should().Be(chaveDoCodigo);
    }

    private static (string Tipo, ServiceLifetime Vida) Registro(Type servico, ServiceLifetime vida) =>
        (servico.ToString(), vida);

    private static (string Tipo, ServiceLifetime Vida)[] Sequencia(IServiceCollection servicos) =>
        [.. servicos.Select(d => (d.ServiceType.ToString(), d.Lifetime))];

    private static IConfiguration ConfiguracaoDaSecao(string chave, string valor) =>
        ConfiguracaoEmMemoria((chave, valor));

    private static IConfiguration ConfiguracaoEmMemoria(params (string Chave, string Valor)[] valores) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Chave, v.Valor)))
            .Build();

    private static IReadOnlyList<HealthCheckRegistration> VerificacoesDeSaude(params (string Chave, string Valor)[] valores)
    {
        ServiceCollection servicos = new();
        servicos.AddOptions();
        servicos.AddGeoHealthChecks(ConfiguracaoEmMemoria(valores), "GeoDb");

        using ServiceProvider provedor = servicos.BuildServiceProvider();
        return [.. provedor.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }

    private static RequestLoggingOptions OpcoesDaRequisicao(
        IConfiguration? configuracao = null,
        Action<RequestLoggingOptions>? configure = null)
    {
        ServiceCollection servicos = new();
        servicos.AddRequestLogging(configuracao, configure);

        using ServiceProvider provedor = servicos.BuildServiceProvider();
        return provedor.GetRequiredService<IOptions<RequestLoggingOptions>>().Value;
    }

    private static EncryptionOptions OpcoesDeCifragem(IConfiguration? configuracao, Action<EncryptionOptions>? configure)
    {
        ServiceCollection servicos = new();
        servicos.AddGeoEncryption(configuracao, configure);

        using ServiceProvider provedor = servicos.BuildServiceProvider();
        return provedor.GetRequiredService<IOptions<EncryptionOptions>>().Value;
    }
}
