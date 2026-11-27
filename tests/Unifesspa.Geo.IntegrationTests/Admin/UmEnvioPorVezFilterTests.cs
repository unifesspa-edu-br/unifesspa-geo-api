namespace Unifesspa.Geo.IntegrationTests.Admin;

using AwesomeAssertions;

using Microsoft.AspNetCore.Http;

using Unifesspa.Geo.API.Filters;

/// <summary>Um envio por vez nesta instância: o segundo é recusado com 409 antes de ler o corpo, e a permissão é devolvida em todo caminho.</summary>
public sealed class UmEnvioPorVezFilterTests : IDisposable
{
    private readonly EnvioDoPacoteEmCurso _emCurso = new();
    private readonly UmEnvioPorVezFilter _filtro;

    public UmEnvioPorVezFilterTests()
    {
        _filtro = new UmEnvioPorVezFilter(_emCurso, new MapeadorDeErrosParaTeste());
    }

    public void Dispose() => _emCurso.Dispose();

    [Fact(DisplayName = "Sem outro envio nem carga em andamento, o envio segue e a permissão é devolvida ao fim")]
    public async Task SemConcorrencia_SegueEDevolveAPermissao()
    {
        ContextoDeFiltroParaTeste contexto = Contexto();

        await _filtro.OnResourceExecutionAsync(contexto.Contexto, contexto.Proximo);

        contexto.RestanteExecutado.Should().BeTrue();
        PermissaoEstaLivre().Should().BeTrue("a permissão foi devolvida ao fim do envio");
    }

    [Fact(DisplayName = "Com outro envio sendo recebido, o segundo retorna 409 sem executar o restante")]
    public async Task ComOutroEnvioEmCurso_Retorna409()
    {
        using IDisposable? primeiro = _emCurso.TentarIniciar();
        ContextoDeFiltroParaTeste contexto = Contexto();

        await _filtro.OnResourceExecutionAsync(contexto.Contexto, contexto.Proximo);

        contexto.StatusDaRecusa.Should().Be(StatusCodes.Status409Conflict);
        contexto.RestanteExecutado.Should().BeFalse();
    }

    [Fact(DisplayName = "Recusa do segundo envio não devolve a permissão do primeiro")]
    public async Task RecusaDoSegundo_MantemAPermissaoDoPrimeiro()
    {
        using IDisposable? primeiro = _emCurso.TentarIniciar();
        ContextoDeFiltroParaTeste contexto = Contexto();
        await _filtro.OnResourceExecutionAsync(contexto.Contexto, contexto.Proximo);

        _emCurso.TentarIniciar().Should().BeNull();
    }

    [Fact(DisplayName = "Se o restante do envio lança exceção, a permissão é devolvida")]
    public async Task ExcecaoNoRestante_DevolveAPermissao()
    {
        ContextoDeFiltroParaTeste contexto = Contexto(new InvalidOperationException("falha do teste"));

        Func<Task> envio = () => _filtro.OnResourceExecutionAsync(contexto.Contexto, contexto.Proximo);

        await envio.Should().ThrowAsync<InvalidOperationException>();
        PermissaoEstaLivre().Should().BeTrue();
    }

    private bool PermissaoEstaLivre()
    {
        using IDisposable? permissao = _emCurso.TentarIniciar();
        return permissao is not null;
    }

    private static ContextoDeFiltroParaTeste Contexto(Exception? falhaNoRestante = null) =>
        new(ContextoDeFiltroParaTeste.Requisicao("application/zip", 10), falhaNoRestante);
}
