namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Limites fixos de segurança aplicados ao pacote dos Correios.</summary>
internal static class LimitesDoPacoteDne
{
    public const int EntradasPorPacote = 16;

    public const int CaracteresNoNomeDaEntrada = 128;

    public const int BytesPorLinha = 64 * 1024;

    public const long BytesPorEntrada = 768L * 1024 * 1024;

    public const long BytesNoTotal = 1024L * 1024 * 1024;

    /// <summary>
    /// Quantos bytes a próxima entrada pode descompactar: o limite por entrada, reduzido
    /// pelo que o pacote já descompactou do limite total.
    /// </summary>
    public static long BytesRestantesParaEntrada(long bytesJaDescompactados) =>
        Math.Min(BytesPorEntrada, Math.Max(0, BytesNoTotal - bytesJaDescompactados));
}
