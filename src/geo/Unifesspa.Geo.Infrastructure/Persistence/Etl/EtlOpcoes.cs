namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>
/// Configuração da carga da base dos Correios, seção <c>Geo:Etl</c>. Os padrões são seguros
/// para produção e teste: o processo em segundo plano fica ligado (consome os envios do endpoint admin).
/// </summary>
public sealed class EtlOpcoes
{
    /// <summary>Caminho da seção de configuração.</summary>
    public const string SectionName = "Geo:Etl";

    /// <summary>Padrão de <see cref="QuantidadeMaximaDeEntradasDoPacote"/>.</summary>
    public const int QuantidadePadraoDeEntradasDoPacote = 32;

    /// <summary>
    /// Liga o <c>BackgroundService</c> que consome a fila e executa as cargas. Default
    /// <see langword="true"/> — manter ligado em produção: com ele desligado, o endpoint ainda
    /// registra a execução <c>EmAndamento</c> e responde <c>202</c>, mas nada a consome, e ela
    /// bloqueia novos disparos por <c>409</c>. Só desligar em teste ou janela de manutenção controlada.
    /// </summary>
    public bool ProcessoEmSegundoPlanoHabilitado { get; set; } = true;

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

    /// <summary>
    /// Tamanho máximo, em bytes, do pacote enviado. Padrão de 128 MiB; deve ser maior que zero.
    /// </summary>
    public long TamanhoMaximoDoPacote { get; set; } = 128L * 1024 * 1024;

    /// <summary>
    /// Quantidade máxima de entradas do zip (arquivos e pasta, todas contadas). Padrão de 32; deve
    /// estar entre 16 e 256.
    /// </summary>
    public int QuantidadeMaximaDeEntradasDoPacote { get; set; } = QuantidadePadraoDeEntradasDoPacote;

    /// <summary>
    /// Diretório onde o pacote enviado fica enquanto a carga não popula o esquema de carga
    /// (em produção, o volume em memória do pod). Padrão: <c>pacotes-dne</c> no diretório temporário do sistema.
    /// </summary>
    public string DiretorioDoPacote { get; set; } = Path.Combine(Path.GetTempPath(), "pacotes-dne");
}
