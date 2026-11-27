namespace Unifesspa.Geo.IntegrationTests.Autenticacao;

using System.Security.Claims;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Core.Authentication;

/// <summary>
/// A transformação copia os papéis do claim realm_access para claims de papel, sem repetir, sem papéis em branco
/// ou que não são texto, e sem alterar o principal anônimo nem o de JSON inválido.
/// </summary>
public sealed class TransformacaoDosPapeisDoRealmTests
{
    [Fact(DisplayName = "Papéis do realm_access viram claims de papel")]
    public async Task PapeisDoRealmViramClaimsDePapel()
    {
        ClaimsPrincipal principal = Autenticado(new Claim("realm_access", """{"roles":["candidato","avaliador"]}"""));

        await Transformar(principal);

        Papeis(principal).Should().Equal("candidato", "avaliador");
    }

    [Fact(DisplayName = "Papel repetido no token ou já presente no principal não é repetido")]
    public async Task PapelRepetidoNaoERepetido()
    {
        ClaimsPrincipal principal = Autenticado(
            new Claim(ClaimTypes.Role, "candidato"),
            new Claim("realm_access", """{"roles":["candidato","avaliador","avaliador"]}"""));

        await Transformar(principal);

        Papeis(principal).Should().Equal("candidato", "avaliador");
    }

    [Fact(DisplayName = "Executar a transformação duas vezes não repete papéis")]
    public async Task ExecutarDuasVezesNaoRepetePapeis()
    {
        ClaimsPrincipal principal = Autenticado(new Claim("realm_access", """{"roles":["candidato"]}"""));

        await Transformar(principal);
        await Transformar(principal);

        Papeis(principal).Should().Equal("candidato");
    }

    [Fact(DisplayName = "A comparação de papéis já presentes diferencia maiúsculas de minúsculas")]
    public async Task ComparacaoDiferenciaMaiusculasDeMinusculas()
    {
        ClaimsPrincipal principal = Autenticado(
            new Claim(ClaimTypes.Role, "Candidato"),
            new Claim("realm_access", """{"roles":["candidato"]}"""));

        await Transformar(principal);

        Papeis(principal).Should().Equal("Candidato", "candidato");
    }

    [Fact(DisplayName = "Elementos que não são texto e papéis em branco são descartados")]
    public async Task ElementosQueNaoSaoTextoEPapeisEmBrancoSaoDescartados()
    {
        ClaimsPrincipal principal = Autenticado(
            new Claim("realm_access", """{"roles":[1,null,{"a":1},true," ","","avaliador"]}"""));

        await Transformar(principal);

        Papeis(principal).Should().Equal("avaliador");
    }

    [Theory(DisplayName = "JSON inválido, sem array de papéis ou claim em branco deixa o principal como está")]
    [InlineData("{isto não é json")]
    [InlineData("""{"outra":["a"]}""")]
    [InlineData("""{"roles":"candidato"}""")]
    [InlineData("""{"roles":[]}""")]
    [InlineData("""{"roles":[" ",2]}""")]
    [InlineData("  ")]
    public async Task ClaimSemPapeisUteisDeixaOPrincipalComoEsta(string valor)
    {
        ClaimsPrincipal principal = Autenticado(new Claim("realm_access", valor));

        ClaimsPrincipal resultado = await Transformar(principal);

        resultado.Should().BeSameAs(principal);
        Papeis(principal).Should().BeEmpty();
    }

    [Fact(DisplayName = "Principal sem o claim realm_access não muda")]
    public async Task PrincipalSemRealmAccessNaoMuda()
    {
        ClaimsPrincipal principal = Autenticado(new Claim("sub", "sub-fictício"));

        await Transformar(principal);

        principal.Claims.Should().ContainSingle();
    }

    [Fact(DisplayName = "Principal anônimo não é tocado, mesmo com realm_access")]
    public async Task PrincipalAnonimoNaoETocado()
    {
        ClaimsPrincipal anonimo = new(new ClaimsIdentity([new Claim("realm_access", """{"roles":["candidato"]}""")]));

        ClaimsPrincipal resultado = await Transformar(anonimo);

        resultado.Should().BeSameAs(anonimo);
        Papeis(anonimo).Should().BeEmpty();
    }

    [Fact(DisplayName = "Principal nulo é recusado")]
    public async Task PrincipalNuloERecusado()
    {
        Func<Task> transformacao = () => new KeycloakRolesClaimsTransformation().TransformAsync(null!);

        await transformacao.Should().ThrowAsync<ArgumentNullException>();
    }

    private static ClaimsPrincipal Autenticado(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Teste"));

    private static Task<ClaimsPrincipal> Transformar(ClaimsPrincipal principal) =>
        new KeycloakRolesClaimsTransformation().TransformAsync(principal);

    private static string[] Papeis(ClaimsPrincipal principal) =>
        [.. principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)];
}
