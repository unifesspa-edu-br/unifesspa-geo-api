namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>
/// Configuração do ETL de atualização periódica do Geo (Story #674), seção
/// <c>Geo:Etl</c>. Os defaults são seguros para produção/teste: o worker fica ligado
/// (consome disparos do endpoint admin) e o seed de desenvolvimento fica
/// <strong>desligado</strong> — só a compose de dev o liga, garantindo que os testes
/// (que rodam como <c>Development</c>) nunca semeiem.
/// </summary>
public sealed class EtlOpcoes
{
    /// <summary>Caminho da seção de configuração.</summary>
    public const string SectionName = "Geo:Etl";

    /// <summary>
    /// Liga o <c>BackgroundService</c> que consome a fila e executa as cargas. Default
    /// <see langword="true"/> — manter ligado em produção: com ele desligado, o endpoint ainda
    /// registra a execução <c>EmAndamento</c> e responde <c>202</c>, mas nada a consome, e ela
    /// bloqueia novos disparos por <c>409</c> até a reconciliação por idade. Só desligar em
    /// teste (determinismo de 202/409) ou janela de manutenção controlada.
    /// </summary>
    public bool WorkerHabilitado { get; set; } = true;

    /// <summary>Liga o seed automático no boot de desenvolvimento (base vazia). Default <see langword="false"/>.</summary>
    public bool SeedHabilitado { get; set; }

    /// <summary>Versão (AAAAMM) do dataset usada pelo seed de desenvolvimento.</summary>
    public string VersaoSeed { get; set; } = "202601";

    /// <summary>
    /// Schema onde os dumps DNE são restaurados no banco de staging dedicado
    /// (<c>ConnectionStrings:GeoStagingDb</c>, issue #12), lido pela <c>DneStagingFonte</c>.
    /// Default <c>public</c> — os dumps (Navicat) qualificam <c>"public"."tbl_cep_..."</c>
    /// explicitamente, então o <c>public</c> nativo do banco de staging os recebe sem
    /// nenhuma reescrita de schema.
    /// </summary>
    public string StagingSchema { get; set; } = "public";

    /// <summary>
    /// Idade a partir da qual uma execução <c>EmAndamento</c> é considerada abandonada
    /// (crash/restart) e reconciliada como falha no startup do worker. O limite folgado
    /// (default 6h, &gt;&gt; a duração esperada de uma carga) evita que, num rolling deploy
    /// multi-réplica, uma instância nova marque como falha uma carga ainda ativa em outra
    /// — a reconciliação só toca execuções antigas o bastante para serem seguramente órfãs.
    /// </summary>
    public TimeSpan LimiteAbandono { get; set; } = TimeSpan.FromHours(6);
}
