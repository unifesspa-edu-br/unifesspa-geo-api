namespace Unifesspa.Geo.IntegrationTests.Inicializacao;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Infrastructure;
using Unifesspa.Geo.Infrastructure.Core.DependencyInjection;
using Unifesspa.Geo.Infrastructure.Persistence;

using Wolverine.Runtime;

/// <summary>
/// Reconhece, entre os serviços hospedados da API, os passos da partida. Migrações e gatilhos
/// da carga são reconhecidos pelos tipos que as próprias extensões de registro adicionam;
/// a mensageria é o serviço que é o <see cref="IWolverineRuntime"/> do contêiner.
/// </summary>
internal sealed class PassosDaInicializacao
{
    private readonly HashSet<Type> _tiposDasMigracoes =
        TiposRegistrados(static servicos => servicos.AddDbContextMigrationsOnStartup<GeoDbContext>());

    private readonly HashSet<Type> _tiposDosGatilhos =
        TiposRegistrados(static servicos => servicos.AddGeoEtlGatilhos(new ConfigurationBuilder().Build()));

    public PassoDaInicializacao? Identificar(ServiceDescriptor descritor, object servico, IWolverineRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(descritor);

        if (ReferenceEquals(servico, runtime))
        {
            return PassoDaInicializacao.Mensageria;
        }

        return PassoDoTipo(descritor.ImplementationType);
    }

    private PassoDaInicializacao? PassoDoTipo(Type? tipo)
    {
        if (tipo is null)
        {
            return null;
        }

        if (_tiposDasMigracoes.Contains(tipo))
        {
            return PassoDaInicializacao.MigracoesDoBanco;
        }

        return _tiposDosGatilhos.Contains(tipo) ? PassoDaInicializacao.GatilhosDaCarga : null;
    }

    private static HashSet<Type> TiposRegistrados(Action<IServiceCollection> registrar)
    {
        ServiceCollection servicos = [];
        registrar(servicos);
        return [.. servicos.Select(static d => d.ImplementationType).OfType<Type>()];
    }
}
