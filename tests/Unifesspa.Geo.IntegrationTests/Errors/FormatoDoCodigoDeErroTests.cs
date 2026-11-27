namespace Unifesspa.Geo.IntegrationTests.Errors;

using System.Text.RegularExpressions;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Infrastructure.Core.Errors;
using Unifesspa.Geo.Infrastructure.Core.OpenApi;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>Todo código de erro que o Geo emite casa com o formato que o contrato declara para o campo code.</summary>
public sealed class FormatoDoCodigoDeErroTests : IClassFixture<GeoOpenApiFactory>
{
    private readonly GeoOpenApiFactory _fabrica;

    public FormatoDoCodigoDeErroTests(GeoOpenApiFactory fabrica)
    {
        _fabrica = fabrica;
    }

    [Fact(DisplayName = "Todos os códigos dos registros do Geo casam com o padrão do campo code do contrato")]
    public void CodigosRegistrados_CasamComOPadraoDoContrato()
    {
        Regex padrao = new(ProblemDetailsExtensionsDocumentTransformer.PadraoDoCodigo, RegexOptions.None, TimeSpan.FromSeconds(1));
        string[] codigos =
        [
            .. _fabrica.Services.GetServices<IDomainErrorRegistration>()
                .SelectMany(static registro => registro.GetMappings())
                .Select(static mapeamento => mapeamento.Value.Code),
        ];

        codigos.Should().Contain("uniplus.validacao").And.Contain("uniplus.geo.cep.formato_invalido");
        codigos.Where(codigo => !padrao.IsMatch(codigo)).Should().BeEmpty();
    }

    [Theory(DisplayName = "O padrão do contrato recusa o que não é código publicado")]
    [InlineData("geo.cep.formato_invalido")]
    [InlineData("uniplus")]
    [InlineData("uniplus.Geo.Cep")]
    [InlineData("uniplus.geo.cep-formato")]
    [InlineData("uniplus..cep")]
    public void PadraoDoContrato_RecusaFormatoInvalido(string codigo)
    {
        Regex.IsMatch(codigo, ProblemDetailsExtensionsDocumentTransformer.PadraoDoCodigo, RegexOptions.None, TimeSpan.FromSeconds(1))
            .Should().BeFalse();
    }
}
