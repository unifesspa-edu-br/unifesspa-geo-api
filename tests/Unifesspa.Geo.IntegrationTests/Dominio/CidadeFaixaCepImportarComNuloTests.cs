namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Results;

public sealed class CidadeFaixaCepImportarComNuloTests
{
    private static readonly ProvenienciaDaCarga Proveniencia = new("202601");

    [Fact(DisplayName = "Importar faixa de CEP da Cidade com o CEP inicial nulo devolve a recusa de CEP inicial obrigatório, sem lançar exceção")]
    public void Importar_CepInicialNulo_DevolveRecusaDeCepInicialObrigatorio()
    {
        Result<CidadeFaixaCep>? resultado = null;

        Action importar = () => resultado = CidadeFaixaCep.Importar(
            Guid.NewGuid(), new FaixaDeCep(null, "68500999"), Proveniencia);

        importar.Should().NotThrow();
        resultado!.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoReferenceDataErrorCodes.CidadeFaixaCepInicialObrigatorio);
    }

    [Fact(DisplayName = "Importar faixa de CEP da Cidade com o CEP final nulo devolve a recusa de CEP final obrigatório, sem lançar exceção")]
    public void Importar_CepFinalNulo_DevolveRecusaDeCepFinalObrigatorio()
    {
        Result<CidadeFaixaCep>? resultado = null;

        Action importar = () => resultado = CidadeFaixaCep.Importar(
            Guid.NewGuid(), new FaixaDeCep("68500000", null), Proveniencia);

        importar.Should().NotThrow();
        resultado!.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoReferenceDataErrorCodes.CidadeFaixaCepFinalObrigatorio);
    }
}
