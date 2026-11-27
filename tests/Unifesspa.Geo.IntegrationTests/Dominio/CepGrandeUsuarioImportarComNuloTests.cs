namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Kernel.Results;

public sealed class CepGrandeUsuarioImportarComNuloTests
{
    [Fact(DisplayName = "Importar CEP de grande usuário com o CEP nulo devolve a recusa de CEP obrigatório, sem lançar exceção")]
    public void Importar_CepNulo_DevolveRecusaDeCepObrigatorio()
    {
        Result<CepGrandeUsuario>? resultado = null;

        Action importar = () => resultado = CepGrandeUsuario.Importar(
            null!, new NomeComFormaDeBusca("Universidade Fictícia", "universidade ficticia"), new ProvenienciaDaCarga("202601"));

        importar.Should().NotThrow();
        resultado!.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoReferenceDataErrorCodes.CepGrandeUsuarioCepObrigatorio);
    }
}
