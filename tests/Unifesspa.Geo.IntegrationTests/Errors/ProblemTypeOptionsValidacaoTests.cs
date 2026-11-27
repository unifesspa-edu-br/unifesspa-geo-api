namespace Unifesspa.Geo.IntegrationTests.Errors;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Core.Errors;

/// <summary>A base do endereço dos erros é obrigatória, absoluta, HTTPS e sem consulta nem fragmento.</summary>
public sealed class ProblemTypeOptionsValidacaoTests
{
    [Theory(DisplayName = "Base do endereço recusada diz o que corrigir")]
    [InlineData(null, "é obrigatório")]
    [InlineData("", "é obrigatório")]
    [InlineData("http://portal.exemplo.test/erros/", "deve usar HTTPS")]
    [InlineData("/erros/", "deve ser um endereço absoluto")]
    [InlineData("portal.exemplo.test/erros/", "deve ser um endereço absoluto")]
    [InlineData("https://portal.exemplo.test/erros/?versao=1", "não aceita consulta nem fragmento")]
    [InlineData("https://portal.exemplo.test/erros/#topo", "não aceita consulta nem fragmento")]
    [InlineData("https://usuario:segredo@portal.exemplo.test/erros/", "não aceita usuário nem senha")]
    [InlineData("https://usuario@portal.exemplo.test/erros/", "não aceita usuário nem senha")]
    public void BaseInvalida_FalhaComMensagemQueDizOQueCorrigir(string? baseDoEndereco, string trechoDaMensagem)
    {
        using ServiceProvider servicos = ServicosDeErroParaTeste.Criar(baseDoEndereco);

        Action ler = () => _ = servicos.GetRequiredService<IOptions<ProblemTypeOptions>>().Value;

        ler.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain("ProblemType:BaseUri").And.Contain(trechoDaMensagem).And.NotContain("segredo");
    }

    [Theory(DisplayName = "Base do endereço válida é aceita, com ou sem barra final")]
    [InlineData("https://portal.exemplo.test/erros/")]
    [InlineData("https://portal.exemplo.test/erros")]
    public void BaseValida_EAceita(string baseDoEndereco)
    {
        using ServiceProvider servicos = ServicosDeErroParaTeste.Criar(baseDoEndereco);

        ProblemTypeOptions opcoes = servicos.GetRequiredService<IOptions<ProblemTypeOptions>>().Value;

        opcoes.BaseUri.Should().NotBeNull();
    }

    [Theory(DisplayName = "O endereço do erro é a base com uma barra e o código, qualquer que seja a barra final configurada")]
    [InlineData("https://portal.exemplo.test/erros/")]
    [InlineData("https://portal.exemplo.test/erros")]
    public void EnderecoDoErro_EBaseMaisCodigo(string baseDoEndereco)
    {
        using ServiceProvider servicos = ServicosDeErroParaTeste.Criar(baseDoEndereco);

        Uri endereco = servicos.GetRequiredService<IDomainErrorMapper>().GetProblemTypeUri("uniplus.cpf.invalido");

        endereco.AbsoluteUri.Should().Be("https://portal.exemplo.test/erros/uniplus.cpf.invalido");
    }
}
