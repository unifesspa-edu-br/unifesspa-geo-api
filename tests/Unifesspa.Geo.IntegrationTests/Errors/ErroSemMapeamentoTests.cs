namespace Unifesspa.Geo.IntegrationTests.Errors;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Infrastructure.Core.Errors;
using Unifesspa.Geo.Kernel.Results;

/// <summary>Erro de domínio sem mapeamento é falha do serviço: nunca vira resposta de erro do cliente.</summary>
public sealed class ErroSemMapeamentoTests : IDisposable
{
    private readonly ServiceProvider _servicos = ServicosDeErroParaTeste.Criar();

    public void Dispose() => _servicos.Dispose();

    [Fact(DisplayName = "Código desconhecido em ToActionResult lança falha que cita só o código interno")]
    public void CodigoDesconhecido_LancaFalhaComOCodigoInterno()
    {
        IDomainErrorMapper mapeador = _servicos.GetRequiredService<IDomainErrorMapper>();
        Result falha = Result.Failure(new DomainError("Teste.SemMapeamento", "texto que não deve aparecer na falha"));

        Action converter = () => falha.ToActionResult(mapeador);

        converter.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Teste.SemMapeamento").And.NotContain("texto que não deve aparecer");
    }

    [Fact(DisplayName = "Código desconhecido em Result genérico também lança falha")]
    public void CodigoDesconhecidoEmResultGenerico_Lanca()
    {
        IDomainErrorMapper mapeador = _servicos.GetRequiredService<IDomainErrorMapper>();
        Result<int> falha = Result<int>.Failure(new DomainError("Teste.SemMapeamento", "detalhe"));

        Action converter = () => falha.ToActionResult(mapeador);

        converter.Should().Throw<InvalidOperationException>();
    }
}
