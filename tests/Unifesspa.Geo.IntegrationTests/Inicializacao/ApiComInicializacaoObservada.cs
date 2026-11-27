namespace Unifesspa.Geo.IntegrationTests.Inicializacao;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Wolverine.Runtime;

/// <summary>
/// Sobe a API de verdade, sem banco, e anota a ordem em que o host inicia as migrações, os
/// gatilhos da carga e a mensageria. Cada serviço hospedado é trocado, na mesma posição, por um
/// observador, de modo que a ordem de registro no <c>Program</c> é a ordem observada.
/// </summary>
public sealed class ApiComInicializacaoObservada : IAsyncLifetime, IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _raiz = new();
    private readonly PassosDaInicializacao _passos = new();
    private readonly List<IHostedService> _originais = [];
    private readonly Lock _trava = new();
    private WebApplicationFactory<Program>? _fabrica;

    internal OrdemObservadaDaInicializacao Ordem { get; } = new();

    /// <summary>Todos os serviços hospedados originais da API, antes de serem envolvidos pelo observador.</summary>
    internal IReadOnlyList<IHostedService> ServicosOriginais
    {
        get
        {
            lock (_trava)
            {
                return [.. _originais];
            }
        }
    }

    public Task InitializeAsync()
    {
        _fabrica = _raiz.WithWebHostBuilder(Configurar);
        _ = _fabrica.Services;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_fabrica is not null)
        {
            await _fabrica.DisposeAsync();
        }

        await _raiz.DisposeAsync();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    private void Configurar(IWebHostBuilder construtor)
    {
        construtor.UseEnvironment("Development");
        construtor.UseSetting("Observability:Enabled", "false");
        construtor.ConfigureAppConfiguration(static (_, configuracao) =>
            configuracao.AddInMemoryCollection(
            [
                new("ConnectionStrings:GeoDb", "Host=localhost;Port=1;Database=geo_ordem;Username=u;Password=p"),
                new("Auth:Authority", "http://localhost/test-realm"),
            ]));
        construtor.ConfigureTestServices(ObservarServicosHospedados);
    }

    private void ObservarServicosHospedados(IServiceCollection servicos)
    {
        for (int i = 0; i < servicos.Count; i++)
        {
            ServiceDescriptor original = servicos[i];
            if (original.ServiceType == typeof(IHostedService) && original.ServiceKey is null)
            {
                servicos[i] = ServiceDescriptor.Singleton<IHostedService>(sp => Observar(original, sp));
            }
        }
    }

    private ServicoHospedadoObservado Observar(ServiceDescriptor descritor, IServiceProvider sp)
    {
        // Só o que o observador cria por tipo é dele; instância pronta e fábrica (como a do
        // Wolverine, que devolve o runtime do contêiner) já têm dono.
        bool criadoAqui = descritor.ImplementationType is not null;
        IHostedService servico = CriarOriginal(descritor, sp);
        lock (_trava)
        {
            _originais.Add(servico);
        }

        PassoDaInicializacao? passo = _passos.Identificar(descritor, servico, sp.GetRequiredService<IWolverineRuntime>());
        return new ServicoHospedadoObservado(servico, passo, Ordem, criadoAqui);
    }

    private static IHostedService CriarOriginal(ServiceDescriptor descritor, IServiceProvider sp) =>
        (IHostedService)(descritor.ImplementationInstance
            ?? descritor.ImplementationFactory?.Invoke(sp)
            ?? ActivatorUtilities.CreateInstance(sp, descritor.ImplementationType!));
}
