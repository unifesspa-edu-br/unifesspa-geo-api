namespace Unifesspa.Geo.IntegrationTests.Autenticacao;

using System.Security.Claims;

using AwesomeAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Infrastructure.Core.Authentication;
using Unifesspa.Geo.IntegrationTests.TestSupport;

/// <summary>
/// Claim de papéis com JSON válido cuja raiz não é um objeto (lista, texto, número ou nulo) é tratado como
/// JSON inválido: o contexto do usuário avisa e fica sem papéis, e a transformação deixa o principal como está,
/// sem lançar exceção.
/// </summary>
public sealed class ClaimDePapeisComRaizQueNaoEObjetoTests
{
    [Theory(DisplayName = "Realm_access cuja raiz não é objeto dá papéis vazios e um aviso")]
    [InlineData("[1]")]
    [InlineData("\"texto\"")]
    [InlineData("7")]
    [InlineData("null")]
    public void RealmComRaizQueNaoEObjetoDaPapeisVaziosEUmAviso(string valor)
    {
        CapturaDeRegistros captura = new();

        UsuarioCom(captura, new Claim("realm_access", valor)).Roles.Should().BeEmpty();

        RegistroCapturado aviso = captura.Registros.Should().ContainSingle().Subject;
        aviso.Nivel.Should().Be(LogLevel.Warning);
        aviso.Mensagem.Should().Contain("realm_access");
    }

    [Theory(DisplayName = "Resource_access cuja raiz não é objeto dá lista vazia e um aviso com o recurso")]
    [InlineData("[1]")]
    [InlineData("\"texto\"")]
    public void RecursoComRaizQueNaoEObjetoDaListaVaziaEUmAviso(string valor)
    {
        CapturaDeRegistros captura = new();

        UsuarioCom(captura, new Claim("resource_access", valor)).GetResourceRoles("uniplus-web").Should().BeEmpty();

        RegistroCapturado aviso = captura.Registros.Should().ContainSingle().Subject;
        aviso.Nivel.Should().Be(LogLevel.Warning);
        aviso.Mensagem.Should().Contain("resource_access").And.Contain("uniplus-web");
    }

    [Theory(DisplayName = "A transformação deixa o principal como está quando a raiz do realm_access não é objeto")]
    [InlineData("[1]")]
    [InlineData("\"texto\"")]
    [InlineData("7")]
    public async Task TransformacaoComRaizQueNaoEObjetoDeixaOPrincipalComoEsta(string valor)
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim("realm_access", valor)], "Teste"));

        ClaimsPrincipal resultado = await new KeycloakRolesClaimsTransformation().TransformAsync(principal);

        resultado.Should().BeSameAs(principal);
        principal.FindAll(ClaimTypes.Role).Should().BeEmpty();
    }

    private static HttpUserContext UsuarioCom(CapturaDeRegistros captura, params Claim[] claims)
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, "Teste"));
        HttpContextAccessor acessor = new() { HttpContext = new DefaultHttpContext { User = principal } };
        return new HttpUserContext(acessor, captura.Para<HttpUserContext>());
    }
}
