namespace Unifesspa.Geo.Infrastructure.Core.DependencyInjection;

using Errors;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

public static class DomainErrorMappingServiceCollectionExtensions
{
    public static IServiceCollection AddDomainErrorMapper(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddProblemTypeOptions(services, configuration);
        services.AddSingleton<IDomainErrorRegistration, KernelDomainErrorRegistration>();
        services.AddSingleton<IDomainErrorRegistration>(new ErrosDaFronteiraHttpDomainErrorRegistration());
        services.AddSingleton<IDomainErrorMapper>(provider => new DomainErrorMappingRegistry(
            provider.GetServices<IDomainErrorRegistration>(),
            provider.GetRequiredService<ProblemTypeUriFactory>()));
        InstalarRecusaDaLeituraDaRequisicao(services);
        return services;
    }

    private static void AddProblemTypeOptions(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ProblemTypeOptions>()
            .Bind(configuration.GetSection(ProblemTypeOptions.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton<IValidateOptions<ProblemTypeOptions>, ProblemTypeOptionsValidator>();
        services.TryAddSingleton<ProblemTypeUriFactory>();
    }

    // Fica na base da cadeia: cada PostConfigure seguinte (o do cursor) guarda esta fábrica e só a chama
    // quando a falha não é dele. Por isso AddDomainErrorMapper precisa ser registrado antes de AddCursorPagination.
    private static void InstalarRecusaDaLeituraDaRequisicao(IServiceCollection services) =>
        services.PostConfigure<ApiBehaviorOptions>(static opcoes =>
            opcoes.InvalidModelStateResponseFactory = RecusaDaLeituraDaRequisicao.Responder);
}
