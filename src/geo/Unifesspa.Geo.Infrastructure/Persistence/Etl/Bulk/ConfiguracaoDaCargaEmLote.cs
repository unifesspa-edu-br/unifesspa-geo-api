namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Texto de conexão e prazo dos comandos da carga em lote, lidos e conferidos da
/// configuração. É classe (e não <c>record</c>) para que o <c>ToString</c> não imprima a senha
/// do texto de conexão.
/// </summary>
internal sealed class ConfiguracaoDaCargaEmLote
{
    private ConfiguracaoDaCargaEmLote(string textoDeConexao, int prazoDosComandosEmSegundos)
    {
        TextoDeConexao = textoDeConexao;
        PrazoDosComandosEmSegundos = prazoDosComandosEmSegundos;
    }

    public string TextoDeConexao { get; }

    public int PrazoDosComandosEmSegundos { get; }

    // O texto vem da IConfiguration porque o getter do NpgsqlConnection remove a senha depois da
    // primeira abertura (Persist Security Info=false): não dá para reaproveitar o do DbContext.
    public static ConfiguracaoDaCargaEmLote Ler(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string textoDeConexao = configuration.GetConnectionString(GeoInfrastructureRegistration.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{GeoInfrastructureRegistration.ConnectionStringName} não configurada para o COPY do ETL Geo.");

        return new ConfiguracaoDaCargaEmLote(textoDeConexao, LerPrazoDosComandosEmSegundos(configuration));
    }

    private static int LerPrazoDosComandosEmSegundos(IConfiguration configuration)
    {
        EtlOpcoes opcoes = configuration.GetSection(EtlOpcoes.SectionName).Get<EtlOpcoes>() ?? new EtlOpcoes();
        TimeSpan prazo = opcoes.TempoLimiteDosComandosDaCarga;

        if (prazo <= TimeSpan.Zero || prazo.TotalSeconds > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.TempoLimiteDosComandosDaCarga)} deve ser maior que zero e caber em segundos inteiros.");
        }

        return (int)Math.Ceiling(prazo.TotalSeconds);
    }
}
