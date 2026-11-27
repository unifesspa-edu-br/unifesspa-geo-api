namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Microsoft.Extensions.Options;

/// <summary>Confere, na partida, as opções do envio do pacote: tamanho máximo, quantidade de entradas e diretório.</summary>
internal sealed class ValidadorDasOpcoesDoPacote : IValidateOptions<EtlOpcoes>
{
    private const int MinimoDeEntradas = 16;
    private const int MaximoDeEntradas = 256;

    public ValidateOptionsResult Validate(string? name, EtlOpcoes options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> faltas = [];
        if (options.TamanhoMaximoDoPacote <= 0)
        {
            faltas.Add($"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.TamanhoMaximoDoPacote)} deve ser maior que zero.");
        }

        if (options.QuantidadeMaximaDeEntradasDoPacote is < MinimoDeEntradas or > MaximoDeEntradas)
        {
            faltas.Add($"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.QuantidadeMaximaDeEntradasDoPacote)} deve estar entre {MinimoDeEntradas} e {MaximoDeEntradas}.");
        }

        if (string.IsNullOrWhiteSpace(options.DiretorioDoPacote))
        {
            faltas.Add($"{EtlOpcoes.SectionName}:{nameof(EtlOpcoes.DiretorioDoPacote)} deve ser informado.");
        }

        return faltas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(faltas);
    }
}
