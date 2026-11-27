namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Results;

public sealed class CepGeralImportarComNuloTests
{
    [Fact(DisplayName = "Importar CEP geral com o CEP nulo devolve a recusa de CEP obrigatório, sem lançar exceção")]
    public void Importar_CepNulo_DevolveRecusaDeCepObrigatorio()
    {
        Result<CepGeral>? resultado = null;

        Action importar = () => resultado = CepGeral.Importar(
            null!, Guid.NewGuid(), null, "PA", default, new ProvenienciaDaCarga("202601"));

        importar.Should().NotThrow();
        resultado!.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoReferenceDataErrorCodes.CepGeralCepObrigatorio);
    }
}
