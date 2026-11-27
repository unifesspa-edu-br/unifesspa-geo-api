namespace Unifesspa.Geo.Domain.ValueObjects;

/// <summary>
/// Origem de um registro de referência: a versão da base dos Correios de que ele veio e se ele
/// ainda consta nessa versão (<see cref="Vigente"/>). A versão é aparada; texto nulo é recusado.
/// </summary>
public sealed record ProvenienciaDaCarga(string VersaoDaBaseDosCorreios, bool Vigente = true)
{
    public string VersaoDaBaseDosCorreios { get; } =
        VersaoDaBaseDosCorreios?.Trim() ?? throw new ArgumentNullException(nameof(VersaoDaBaseDosCorreios));

    /// <summary>Indica se a versão informada tem conteúdo depois de aparada.</summary>
    public bool TemVersao => !string.IsNullOrWhiteSpace(VersaoDaBaseDosCorreios);
}
