namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.Extensions.Options;

/// <summary>Confere, na partida, o intervalo do sinal de vida da conexão que segura a trava da carga.</summary>
internal sealed class ValidadorDaTravaDaCarga : IValidateOptions<EtlOpcoes>
{
    private static readonly TimeSpan Minimo = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Maximo = TimeSpan.FromMinutes(5);

    public ValidateOptionsResult Validate(string? name, EtlOpcoes options)
    {
        ArgumentNullException.ThrowIfNull(options);

        TimeSpan intervalo = options.IntervaloDoSinalDeVidaDaTrava;
        return intervalo < Minimo || intervalo > Maximo
            ? ValidateOptionsResult.Fail(
                $"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.IntervaloDoSinalDeVidaDaTrava)} deve estar entre 1 segundo e 5 minutos.")
            : ValidateOptionsResult.Success;
    }
}
