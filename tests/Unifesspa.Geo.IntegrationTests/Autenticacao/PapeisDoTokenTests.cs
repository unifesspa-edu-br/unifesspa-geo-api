namespace Unifesspa.Geo.IntegrationTests.Autenticacao;

using System.Security.Claims;

using AwesomeAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Infrastructure.Core.Authentication;
using Unifesspa.Geo.IntegrationTests.TestSupport;

/// <summary>
/// Papéis lidos do token pelo contexto do usuário: papéis do realm e de um recurso, JSON inválido, elementos que
/// não são texto, papéis em branco, repetição e o primeiro valor não vazio de nome, e-mail e identificador.
/// </summary>
public sealed class PapeisDoTokenTests
{
    [Fact(DisplayName = "Papéis do realm_access entram nos papéis do usuário")]
    public void PapeisDoRealmEntramNosPapeisDoUsuario()
    {
        HttpUserContext usuario = UsuarioCom(new CapturaDeRegistros(), new Claim("realm_access", """{"roles":["candidato","avaliador"]}"""));

        usuario.Roles.Should().BeEquivalentTo("candidato", "avaliador");
    }

    [Fact(DisplayName = "Papéis diretos e do realm_access somam sem repetir, ignorando a caixa")]
    public void PapeisDiretosEDoRealmSomamSemRepetir()
    {
        HttpUserContext usuario = UsuarioCom(
            new CapturaDeRegistros(),
            new Claim(ClaimTypes.Role, "Candidato"),
            new Claim("roles", "avaliador"),
            new Claim("realm_access", """{"roles":["candidato","gestor"]}"""));

        usuario.Roles.Should().BeEquivalentTo("Candidato", "avaliador", "gestor");
    }

    [Fact(DisplayName = "Elementos que não são texto e papéis em branco saem dos papéis do usuário")]
    public void ElementosQueNaoSaoTextoEPapeisEmBrancoSaem()
    {
        HttpUserContext usuario = UsuarioCom(
            new CapturaDeRegistros(),
            new Claim("realm_access", """{"roles":["candidato",7,null,{"x":1},true,"  ","","avaliador"]}"""));

        usuario.Roles.Should().BeEquivalentTo("candidato", "avaliador");
    }

    [Theory(DisplayName = "Realm sem o array de papéis, com array de outro tipo ou ausente dá lista vazia sem aviso")]
    [InlineData("""{"outra":["a"]}""")]
    [InlineData("""{"roles":"candidato"}""")]
    [InlineData("""{"roles":{"a":1}}""")]
    [InlineData("   ")]
    public void RealmSemArrayDePapeisDaListaVazia(string valor)
    {
        CapturaDeRegistros captura = new();

        UsuarioCom(captura, new Claim("realm_access", valor)).Roles.Should().BeEmpty();

        captura.Registros.Should().BeEmpty();
    }

    [Fact(DisplayName = "Sem o claim realm_access os papéis ficam vazios")]
    public void SemRealmAccessOsPapeisFicamVazios()
    {
        UsuarioCom(new CapturaDeRegistros()).Roles.Should().BeEmpty();
    }

    [Fact(DisplayName = "JSON inválido no realm_access dá papéis vazios e um aviso que não traz o conteúdo")]
    public void JsonInvalidoNoRealmDaVazioEUmAviso()
    {
        CapturaDeRegistros captura = new();

        UsuarioCom(captura, new Claim("realm_access", "{isto não é json")).Roles.Should().BeEmpty();

        RegistroCapturado aviso = captura.Registros.Should().ContainSingle().Subject;
        aviso.Nivel.Should().Be(LogLevel.Warning);
        aviso.Mensagem.Should().Contain("realm_access").And.NotContain("isto não é json");
        aviso.Excecao.Should().NotBeNull();
    }

    [Fact(DisplayName = "Papéis do recurso vêm do resource_access, só do recurso pedido")]
    public void PapeisDoRecursoVemSoDoRecursoPedido()
    {
        HttpUserContext usuario = UsuarioCom(
            new CapturaDeRegistros(),
            new Claim("resource_access", """{"uniplus-web":{"roles":["editor","editor","leitor"]},"outro":{"roles":["admin"]}}"""));

        usuario.GetResourceRoles("uniplus-web").Should().Equal("editor", "leitor");
        usuario.GetResourceRoles("outro").Should().Equal("admin");
    }

    [Theory(DisplayName = "Recurso ausente, que não é objeto ou sem array de papéis dá lista vazia")]
    [InlineData("""{"outro":{"roles":["a"]}}""")]
    [InlineData("""{"uniplus-web":"texto"}""")]
    [InlineData("""{"uniplus-web":{"outra":[]}}""")]
    [InlineData("""{"uniplus-web":{"roles":"a"}}""")]
    public void RecursoSemPapeisDaListaVazia(string valor)
    {
        CapturaDeRegistros captura = new();

        UsuarioCom(captura, new Claim("resource_access", valor)).GetResourceRoles("uniplus-web").Should().BeEmpty();

        captura.Registros.Should().BeEmpty();
    }

    [Fact(DisplayName = "Elementos que não são texto saem dos papéis do recurso")]
    public void ElementosQueNaoSaoTextoSaemDosPapeisDoRecurso()
    {
        HttpUserContext usuario = UsuarioCom(
            new CapturaDeRegistros(),
            new Claim("resource_access", """{"uniplus-web":{"roles":["editor",1,null,[],"leitor"]}}"""));

        usuario.GetResourceRoles("uniplus-web").Should().Equal("editor", "leitor");
    }

    [Fact(DisplayName = "JSON inválido no resource_access dá lista vazia e um aviso com o recurso")]
    public void JsonInvalidoNoRecursoDaVazioEUmAviso()
    {
        CapturaDeRegistros captura = new();

        UsuarioCom(captura, new Claim("resource_access", "[[[")).GetResourceRoles("uniplus-web").Should().BeEmpty();

        RegistroCapturado aviso = captura.Registros.Should().ContainSingle().Subject;
        aviso.Nivel.Should().Be(LogLevel.Warning);
        aviso.Mensagem.Should().Contain("resource_access").And.Contain("uniplus-web");
    }

    [Fact(DisplayName = "Identificador, nome e e-mail usam o primeiro claim não vazio na ordem de preferência")]
    public void PrimeiroClaimNaoVazioVence()
    {
        HttpUserContext usuario = UsuarioCom(
            new CapturaDeRegistros(),
            new Claim(ClaimTypes.NameIdentifier, "  "),
            new Claim("sub", "sub-fictício"),
            new Claim(ClaimTypes.Name, string.Empty),
            new Claim("name", "Maria Teste"),
            new Claim("preferred_username", "maria.teste"),
            new Claim(ClaimTypes.Email, "maria@exemplo.test"),
            new Claim("email", "outro@exemplo.test"));

        usuario.UserId.Should().Be("sub-fictício");
        usuario.Name.Should().Be("Maria Teste");
        usuario.Email.Should().Be("maria@exemplo.test");
    }

    [Fact(DisplayName = "Sem nenhum claim preenchido, identificador, nome e e-mail são nulos")]
    public void SemClaimPreenchidoOsValoresSaoNulos()
    {
        HttpUserContext usuario = UsuarioCom(new CapturaDeRegistros(), new Claim("sub", " "));

        usuario.UserId.Should().BeNull();
        usuario.Name.Should().BeNull();
        usuario.Email.Should().BeNull();
    }

    [Fact(DisplayName = "Sem contexto HTTP não há usuário autenticado nem papéis")]
    public void SemContextoHttpNaoHaUsuarioNemPapeis()
    {
        HttpUserContext usuario = new(new HttpContextAccessor(), new CapturaDeRegistros().Para<HttpUserContext>());

        usuario.IsAuthenticated.Should().BeFalse();
        usuario.UserId.Should().BeNull();
        usuario.Roles.Should().BeEmpty();
    }

    private static HttpUserContext UsuarioCom(CapturaDeRegistros captura, params Claim[] claims)
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, "Teste"));
        HttpContextAccessor acessor = new() { HttpContext = new DefaultHttpContext { User = principal } };
        return new HttpUserContext(acessor, captura.Para<HttpUserContext>());
    }
}
