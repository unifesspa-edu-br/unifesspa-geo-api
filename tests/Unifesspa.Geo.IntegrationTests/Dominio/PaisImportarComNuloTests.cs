namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Results;

public sealed class PaisImportarComNuloTests
{
    [Fact(DisplayName = "Importar país com a sigla ISO nula devolve a recusa de sigla ISO obrigatória, sem lançar exceção")]
    public void Importar_SiglaIsoNula_DevolveRecusaDeSiglaIsoObrigatoria()
    {
        Result<Pais>? resultado = null;

        Action importar = () => resultado = Pais.Importar(
            null!, "BR", "Brasil", default, new ProvenienciaDaCarga("202601"));

        importar.Should().NotThrow();
        resultado!.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoReferenceDataErrorCodes.PaisSiglaIsoObrigatoria);
    }
}
