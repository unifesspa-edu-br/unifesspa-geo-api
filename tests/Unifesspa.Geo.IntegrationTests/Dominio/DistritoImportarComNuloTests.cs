namespace Unifesspa.Geo.IntegrationTests.Dominio;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

public sealed class DistritoImportarComNuloTests
{
    private static readonly ProvenienciaDaCarga Proveniencia = new("202601");

    [Fact(DisplayName = "Importar Distrito com a forma de busca nula devolve a recusa de nome normalizado obrigatório, sem lançar exceção")]
    public void Importar_NomeNormalizadoNulo_DevolveRecusaDeNomeNormalizadoObrigatorio()
    {
        Result<Distrito>? resultado = null;

        Action importar = () => resultado = Distrito.Importar(
            Guid.NewGuid(), "PA", new NomeComFormaDeBusca("Centro", null), default, null, Proveniencia);

        importar.Should().NotThrow();
        resultado!.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoReferenceDataErrorCodes.DistritoNomeNormalizadoObrigatorio);
    }

    [Fact(DisplayName = "Atualizar Distrito com a forma de busca nula devolve a recusa de nome normalizado obrigatório, sem lançar exceção")]
    public void Atualizar_NomeNormalizadoNulo_DevolveRecusaDeNomeNormalizadoObrigatorio()
    {
        Guid cidadeId = Guid.NewGuid();
        Distrito existente = GeoTestKeys.Ok(Distrito.Importar(
            cidadeId, "PA", new NomeComFormaDeBusca("Centro", "centro"), default, null, Proveniencia));
        Result? resultado = null;

        Action atualizar = () => resultado = existente.Atualizar(
            cidadeId, "PA", new NomeComFormaDeBusca("Centro", null), default, null, Proveniencia);

        atualizar.Should().NotThrow();
        resultado!.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoReferenceDataErrorCodes.DistritoNomeNormalizadoObrigatorio);
    }
}
