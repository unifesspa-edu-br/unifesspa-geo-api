namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.Extensions.Options;

/// <summary>Confere, na partida, as opções do envio do pacote: tamanho máximo e diretório.</summary>
internal sealed class ValidadorDasOpcoesDoPacote : IValidateOptions<EtlOpcoes>
{
    public ValidateOptionsResult Validate(string? name, EtlOpcoes options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> faltas = [];
        if (options.TamanhoMaximoDoPacote <= 0)
        {
            faltas.Add($"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.TamanhoMaximoDoPacote)} deve ser maior que zero.");
        }

        if (string.IsNullOrWhiteSpace(options.DiretorioDoPacote))
        {
            faltas.Add($"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.DiretorioDoPacote)} deve ser informado.");
        }

        return faltas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(faltas);
    }
}
