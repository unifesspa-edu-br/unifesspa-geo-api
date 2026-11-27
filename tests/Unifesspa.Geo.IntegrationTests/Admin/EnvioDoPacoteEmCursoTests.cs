namespace Unifesspa.Geo.IntegrationTests.Admin;

using AwesomeAssertions;

using Unifesspa.Geo.API.Filters;

/// <summary>Permissão local de um envio por vez: recusa sem esperar e só devolve o que é do seu dono.</summary>
public sealed class EnvioDoPacoteEmCursoTests : IDisposable
{
    private readonly EnvioDoPacoteEmCurso _emCurso = new();

    public void Dispose() => _emCurso.Dispose();

    [Fact(DisplayName = "Segundo envio é recusado sem esperar enquanto o primeiro está em curso")]
    public void SegundoEnvio_ERecusadoEnquantoOPrimeiroEstaEmCurso()
    {
        using IDisposable? primeiro = _emCurso.TentarIniciar();

        primeiro.Should().NotBeNull();
        _emCurso.TentarIniciar().Should().BeNull();
    }

    [Fact(DisplayName = "Depois que o primeiro termina, um novo envio é aceito")]
    public void DepoisDoFim_UmNovoEnvioEAceito()
    {
        _emCurso.TentarIniciar()!.Dispose();

        using IDisposable? novo = _emCurso.TentarIniciar();

        novo.Should().NotBeNull();
    }

    [Fact(DisplayName = "Devolver a permissão duas vezes não libera a de outro envio")]
    public void DevolverDuasVezes_NaoLiberaAPermissaoDeOutroEnvio()
    {
        IDisposable primeiro = _emCurso.TentarIniciar()!;
        primeiro.Dispose();
        using IDisposable? segundo = _emCurso.TentarIniciar();

        primeiro.Dispose();

        _emCurso.TentarIniciar().Should().BeNull("o segundo envio ainda está em curso");
    }
}
