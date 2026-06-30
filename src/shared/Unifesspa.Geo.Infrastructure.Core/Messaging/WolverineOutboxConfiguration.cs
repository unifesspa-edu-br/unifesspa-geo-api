namespace Unifesspa.Geo.Infrastructure.Core.Messaging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using Middleware;

using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

/// <summary>
/// Configuração canônica do backbone Wolverine produtivo do UniPlus, conforme
/// ADR-0004 (outbox transacional) e ADR-0005 (cascading messages como drenagem
/// canônica de domain events). Aplicada por <see cref="UseWolverineOutboxCascading"/>
/// no host do módulo (Geo.API).
/// </summary>
public static class WolverineOutboxConfiguration
{
    /// <summary>
    /// Schema PostgreSQL onde Wolverine cria as tabelas de outbox/inbox/scheduled
    /// (<c>wolverine_outgoing_envelopes</c>, <c>wolverine_incoming_envelopes</c>,
    /// <c>wolverine_node_assignments</c> etc.).
    /// </summary>
    public const string PersistenceSchema = "wolverine";

    /// <summary>
    /// Configura o host com Wolverine + outbox transacional Postgres. O messaging
    /// roda inteiramente sobre a queue PG durável (<c>ToPostgresqlQueue</c>); não há
    /// transporte para broker externo no Geo. A drenagem de domain events é feita por
    /// cascading messages (handlers retornam <c>IEnumerable&lt;object&gt;</c>) — sem
    /// <c>PublishDomainEventsFromEntityFrameworkCore</c>, conforme ADR-0005.
    /// </summary>
    /// <param name="host">Host do <see cref="WebApplicationBuilder"/>.</param>
    /// <param name="configuration"><see cref="IConfiguration"/> live do
    /// <see cref="WebApplicationBuilder"/> (passar <c>builder.Configuration</c>).
    /// A leitura da connection string acontece dentro do callback do
    /// <c>UseWolverine</c>, momento em que os providers já foram materializados
    /// — overrides aplicados via env vars ou <c>WebApplicationFactory</c> são
    /// respeitados, desde que entrem em sources que o
    /// <see cref="WebApplicationBuilder"/> consulta (env vars sempre entram;
    /// <c>ConfigureAppConfiguration</c> via <c>IWebHostBuilder</c> não se
    /// propaga para <see cref="WebApplicationBuilder.Configuration"/> em apps
    /// minimal API — usar env vars nos testes).</param>
    /// <param name="connectionStringName">Nome da connection string em
    /// <see cref="IConfiguration.GetConnectionString"/> (ex.: <c>"GeoDb"</c>).</param>
    /// <param name="configureRouting">Callback para roteamento específico do
    /// módulo (ex.: <c>opts.PublishMessage&lt;EditalPublicadoEvent&gt;()
    /// .ToPostgresqlQueue("domain-events")</c>). Executado depois das policies
    /// transacionais; pode adicionar publishers, listeners, dead letters etc.
    /// Pode ser nulo se o módulo ainda não tem eventos a rotear.</param>
    /// <remarks>
    /// <para><strong>Nota sobre <c>Discovery.IncludeAssembly</c> (issue #198):</strong>
    /// hoje o consumidor único (<c>Geo.API/Program.cs</c>) chama
    /// <c>opts.Discovery.IncludeAssembly(typeof(GeoApplicationAssemblyMarker).Assembly)</c>
    /// inline dentro do <paramref name="configureRouting"/>. Quando surgir um segundo
    /// consumidor com handler cascading, refatorar este helper para receber um parâmetro
    /// adicional <c>params Type[] applicationMarkers</c> (ou
    /// <c>IEnumerable&lt;Assembly&gt;</c>) que faça o
    /// <c>opts.Discovery.IncludeAssembly</c> internamente. Não antecipar a
    /// abstração agora — YAGNI até existir o segundo consumidor.</para>
    /// </remarks>
    public static IHostBuilder UseWolverineOutboxCascading(
        this IHostBuilder host,
        IConfiguration configuration,
        string connectionStringName,
        Action<WolverineOptions>? configureRouting = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        return host.UseWolverine(opts =>
        {
            string? connectionString = configuration.GetConnectionString(connectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{connectionStringName}' não configurada — Wolverine não pode inicializar o outbox.");
            }

            // Persistência do outbox no mesmo banco do módulo, schema isolado.
            // EnableMessageTransport(_ => { }) ativa o transporte interno PG queue.
            opts.PersistMessagesWithPostgresql(connectionString, PersistenceSchema)
                .EnableMessageTransport(_ => { });

            // Atomicidade write+evento: o handler que muta agregado e retorna
            // cascading messages tem o envelope persistido na MESMA transação do
            // SaveChanges, via IEnvelopeTransaction instalado por
            // EnrollDbContextInTransaction.
            opts.UseEntityFrameworkCoreTransactions();
            opts.Policies.AutoApplyTransactions();
            opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

            // CorrelationIdEnvelopeMiddleware roda em TODOS os chains (inclusive
            // event handlers consumidos da queue durável) — implementa o terceiro
            // componente da ADR-0052. Registrado ANTES do AddCommandQueryMiddleware para que o
            // escopo do LogContext esteja ativo quando o WolverineLoggingMiddleware
            // emite a entrada inicial "Processando {RequestName}".
            opts.AddCorrelationIdMiddleware();

            // Middleware CQRS canônicos (logging + validação FluentValidation),
            // restritos a chains de ICommand<>/IQuery<> — mensagens internas do
            // Wolverine não atravessam esse pipeline.
            opts.AddCommandQueryMiddleware();

            // Propaga o header `uniplus.correlation-id` de qualquer envelope incoming
            // (HTTP → Send, ou outbox → consumer) para todas as outgoing messages
            // do mesmo handler context — cascading, IMessageBus.PublishAsync, agendamentos.
            // É o que fecha a propagação do CorrelationId exigida pela ADR-0052.
            // O CorrelationIdEnvelopeMiddleware acima garante que o header esteja sempre
            // presente no incoming envelope (gera GUID caso ausente/inválido).
            opts.Policies.PropagateIncomingHeaderToOutgoing(CorrelationIdEnvelopeMiddleware.HeaderName);

            // Discovery do assembly Infrastructure.Core — handlers compartilhados (ex.:
            // SmokePingHandler que processa SmokePingMessage publicada pelo endpoint
            // /api/_smoke/messaging/publish do #346) ficam descobertos sem que cada
            // Program.cs precise repetir o IncludeAssembly. Idempotente — assembly do
            // entry já é scaneado por default; este registro é defensivo para handlers
            // do Core.
            opts.Discovery.IncludeAssembly(typeof(WolverineOutboxConfiguration).Assembly);

            // Schema do Wolverine (tabelas wolverine_outgoing_envelopes, wolverine_incoming_envelopes,
            // wolverine_node_assignments etc.) é auto-criado/atualizado no startup — issue #344.
            // Idempotente: Wolverine inspeciona o schema atual e aplica apenas o delta. Múltiplas
            // réplicas startando simultaneamente são coordenadas pelo lock interno do framework.
            //
            // Decisão (#344): em ambientes Uni+ não há orquestração de schema externa ao host
            // (sem step de "dotnet ef database update" no Helm chart), então delegar a criação
            // ao próprio host é o caminho mais simples e racional para destravar bring-up de
            // pods com banco vazio (standalone/lab). EF Core migrations dos módulos são
            // aplicadas em paralelo por ApplyMigrationsAsync<TContext> no Program.cs.
            opts.AutoBuildMessageStorageOnStartup = JasperFx.AutoCreate.CreateOrUpdate;

            configureRouting?.Invoke(opts);
        });
    }
}
