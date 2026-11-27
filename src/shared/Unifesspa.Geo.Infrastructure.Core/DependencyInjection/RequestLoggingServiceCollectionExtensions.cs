namespace Unifesspa.Geo.Infrastructure.Core.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Middleware;

public static class RequestLoggingServiceCollectionExtensions
{
    public static IServiceCollection AddRequestLogging(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<RequestLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        OptionsBuilder<RequestLoggingOptions> optionsBuilder = services
            .AddOptions<RequestLoggingOptions>()
            .ComConfiguracao(configuration, RequestLoggingOptions.SectionName, configure);

        NormalizarListasAoConfigurar(optionsBuilder);
        ExigirValidacaoNaPartida(services, optionsBuilder);

        services.AddSingleton<QueryStringMasker>();

        return services;
    }

    private static void NormalizarListasAoConfigurar(OptionsBuilder<RequestLoggingOptions> optionsBuilder) =>
        // Binder do IConfiguration agrega sem deduplicar; normalizar in-place.
        optionsBuilder.PostConfigure(opts =>
        {
            NormalizarListaInPlace(opts.NomesParametrosSensiveis);
            NormalizarListaInPlace(opts.PrefixosSilenciados);
        });

    private static void ExigirValidacaoNaPartida(
        IServiceCollection services,
        OptionsBuilder<RequestLoggingOptions> optionsBuilder)
    {
        // ValidateOnStart: falha no boot se appsettings remover masking LGPD.
        services.AddSingleton<IValidateOptions<RequestLoggingOptions>, RequestLoggingOptionsValidator>();
        optionsBuilder.ValidateOnStart();
    }

    // A lista é materializada antes do Clear: enumerar e esvaziar a mesma lista corromperia a leitura.
    private static void NormalizarListaInPlace(IList<string> lista)
    {
        if (lista.Count == 0)
        {
            return;
        }

        string[] normalizada = [.. Distintos(lista)];
        lista.Clear();
        foreach (string item in normalizada)
        {
            lista.Add(item);
        }
    }

    private static IEnumerable<string> Distintos(IEnumerable<string> lista) =>
        lista
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
