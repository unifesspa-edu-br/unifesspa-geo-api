namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>
/// Configuração da carga de atualização periódica do Geo, seção
/// <c>Geo:Etl</c>. Os defaults são seguros para produção/teste: o processo em segundo plano fica ligado
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
    /// bloqueia novos disparos por <c>409</c>. Só desligar em teste ou janela de manutenção controlada.
    /// </summary>
    public bool ProcessoEmSegundoPlanoHabilitado { get; set; } = true;

    /// <summary>Liga o seed automático no boot de desenvolvimento (base vazia). Default <see langword="false"/>.</summary>
    public bool SeedHabilitado { get; set; }

    /// <summary>Versão (AAAAMM) do dataset usada pelo seed de desenvolvimento.</summary>
    public string VersaoSeed { get; set; } = "202610";

    /// <summary>
    /// Schema onde os dumps DNE são restaurados no banco de staging dedicado
    /// (<c>ConnectionStrings:GeoStagingDb</c>, issue #12), lido pela <c>DneStagingFonte</c>.
    /// Default <c>public</c> — os dumps (Navicat) qualificam <c>"public"."tbl_cep_..."</c>
    /// explicitamente, então o <c>public</c> nativo do banco de staging os recebe sem
    /// nenhuma reescrita de schema.
    /// </summary>
    public string StagingSchema { get; set; } = "public";

    /// <summary>
    /// Duração máxima de uma execução da carga antes do cancelamento automático e reversão
    /// integral da transação. Padrão de 1 hora; deve ser maior que zero.
    /// </summary>
    public TimeSpan DuracaoMaximaDaCarga { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Prazo de cada comando longo da carga em lote (mesclagem dos logradouros e dos complementos
    /// e contagens). Vale só para os comandos da carga: as consultas normais da API seguem com o
    /// prazo padrão da conexão. Padrão de 30 minutos; deve ser maior que zero e caber em segundos inteiros.
    /// </summary>
    public TimeSpan TempoLimiteDosComandosDaCarga { get; set; } = TimeSpan.FromMinutes(30);
}
