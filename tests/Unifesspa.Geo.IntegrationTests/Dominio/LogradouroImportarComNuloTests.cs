namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Results;

public sealed class LogradouroImportarComNuloTests
{
    [Fact(DisplayName = "Importar logradouro com a forma de busca nula devolve a recusa de nome normalizado obrigatório, sem lançar exceção")]
    public void Importar_NomeNormalizadoNulo_DevolveRecusaDeNomeNormalizadoObrigatorio()
    {
        Result<Logradouro>? resultado = null;

        Action importar = () => resultado = Logradouro.Importar(
            "68500000",
            new NomeDoLogradouro("Rua", "das Flores", "Rua das Flores", null),
            new LocalidadeDoLogradouro(Guid.NewGuid(), null, null, "PA"),
            default,
            true,
            new ProvenienciaDaCarga("202601"));

        importar.Should().NotThrow();
        resultado!.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoReferenceDataErrorCodes.LogradouroNomeNormalizadoObrigatorio);
    }
}
