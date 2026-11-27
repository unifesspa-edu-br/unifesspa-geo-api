namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.Extensions.Options;

/// <summary>Confere, na partida, os prazos da carga: duração máxima e tempo limite de cada comando longo.</summary>
internal sealed class ValidadorDosPrazosDaCarga : IValidateOptions<EtlOpcoes>
{
    // O cancelamento por prazo aceita no máximo 4294967294 ms (cerca de 49,7 dias).
    internal static readonly TimeSpan DuracaoMaxima = TimeSpan.FromDays(49);

    // O driver converte o prazo para milissegundos em inteiro de 32 bits (cerca de 24,8 dias).
    internal static readonly TimeSpan TempoLimiteMaximo = TimeSpan.FromSeconds(int.MaxValue / 1000);

    public ValidateOptionsResult Validate(string? name, EtlOpcoes options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> faltas = [];
        if (options.DuracaoMaximaDaCarga <= TimeSpan.Zero || options.DuracaoMaximaDaCarga > DuracaoMaxima)
        {
            faltas.Add($"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.DuracaoMaximaDaCarga)} deve ser maior que zero e no máximo 49 dias.");
        }

        if (options.TempoLimiteDosComandosDaCarga <= TimeSpan.Zero || options.TempoLimiteDosComandosDaCarga > TempoLimiteMaximo)
        {
            faltas.Add($"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.TempoLimiteDosComandosDaCarga)} deve ser maior que zero e no máximo 2147483 segundos.");
        }

        return faltas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(faltas);
    }
}
