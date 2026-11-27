namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>O que cada entrada pode descompactar depende do que o pacote já descompactou.</summary>
public sealed class LimitesDoPacoteDneTests
{
    private const long MiB = 1024L * 1024;

    [Theory(DisplayName = "O limite da entrada é o menor entre 768 MiB e o que resta de 1 GiB")]
    [InlineData(0, 768 * MiB)]
    [InlineData(200 * MiB, 768 * MiB)]
    [InlineData(700 * MiB, 324 * MiB)]
    [InlineData(1024 * MiB, 0)]
    [InlineData(2048 * MiB, 0)]
    public void RestanteParaEntrada_RespeitaOTotal(long jaDescompactados, long esperado)
    {
        LimitesDoPacoteDne.BytesRestantesParaEntrada(jaDescompactados).Should().Be(esperado);
    }
}
