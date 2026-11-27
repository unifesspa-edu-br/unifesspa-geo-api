namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Prazo dos comandos longos da carga em lote, lido e conferido da configuração.
/// </summary>
internal sealed class ConfiguracaoDaCargaEmLote
{
    private ConfiguracaoDaCargaEmLote(int prazoDosComandosEmSegundos)
    {
        PrazoDosComandosEmSegundos = prazoDosComandosEmSegundos;
    }

    public int PrazoDosComandosEmSegundos { get; }

    public static ConfiguracaoDaCargaEmLote Ler(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new ConfiguracaoDaCargaEmLote(LerPrazoDosComandosEmSegundos(configuration));
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
