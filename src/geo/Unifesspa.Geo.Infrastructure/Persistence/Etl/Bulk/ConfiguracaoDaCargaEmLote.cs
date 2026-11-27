namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// Prazo dos comandos longos da carga em lote, lido da configuração e conferido pelo mesmo validador da partida.
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
        ValidateOptionsResult resultado = new ValidadorDosPrazosDaCarga().Validate(null, opcoes);

        return resultado.Failed
            ? throw new InvalidOperationException(string.Join(" ", resultado.Failures))
            : (int)Math.Ceiling(opcoes.TempoLimiteDosComandosDaCarga.TotalSeconds);
    }
}
