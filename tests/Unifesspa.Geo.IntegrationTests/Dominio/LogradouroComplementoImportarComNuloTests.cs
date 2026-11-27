namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Results;

public sealed class LogradouroComplementoImportarComNuloTests
{
    private static readonly ProvenienciaDaCarga Proveniencia = new("202601");

    [Fact(DisplayName = "Importar complemento com o CEP nulo devolve a recusa de CEP obrigatório, sem lançar exceção")]
    public void Importar_CepNulo_DevolveRecusaDeCepObrigatorio()
    {
        Result<LogradouroComplemento>? resultado = null;

        Action importar = () => resultado = LogradouroComplemento.Importar(
            null!, "Lado par", "lado par", Proveniencia);

        importar.Should().NotThrow();
        resultado!.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoReferenceDataErrorCodes.LogradouroComplementoCepObrigatorio);
    }

    [Fact(DisplayName = "Importar complemento com o complemento normalizado nulo devolve a recusa de normalizado obrigatório, sem lançar exceção")]
    public void Importar_NormalizadoNulo_DevolveRecusaDeNormalizadoObrigatorio()
    {
        Result<LogradouroComplemento>? resultado = null;

        Action importar = () => resultado = LogradouroComplemento.Importar(
            "68500000", "Lado par", null!, Proveniencia);

        importar.Should().NotThrow();
        resultado!.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoReferenceDataErrorCodes.LogradouroComplementoNormalizadoObrigatorio);
    }
}
