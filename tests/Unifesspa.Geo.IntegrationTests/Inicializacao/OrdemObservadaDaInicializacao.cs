namespace Unifesspa.Geo.IntegrationTests.Inicializacao;

/// <summary>
/// Anotações, na ordem em que aconteceram, do começo e do término de cada passo da partida.
/// O host pode iniciar serviços ao mesmo tempo; por isso toda leitura e escrita passa pela trava.
/// </summary>
internal sealed class OrdemObservadaDaInicializacao
{
    private readonly List<(PassoDaInicializacao Passo, MomentoDoPasso Momento)> _anotacoes = [];
    private readonly Lock _trava = new();

    public void Anotar(PassoDaInicializacao passo, MomentoDoPasso momento)
    {
        lock (_trava)
        {
            _anotacoes.Add((passo, momento));
        }
    }

    public bool Observou(PassoDaInicializacao passo)
    {
        lock (_trava)
        {
            return _anotacoes.Exists(a => a.Passo == passo);
        }
    }

    /// <summary>Posição da primeira anotação de começo do passo; -1 se o passo não foi observado.</summary>
    public int PrimeiroComeco(PassoDaInicializacao passo)
    {
        lock (_trava)
        {
            return _anotacoes.FindIndex(a => a.Passo == passo && a.Momento == MomentoDoPasso.Comecou);
        }
    }

    /// <summary>Posição da última anotação de término do passo; -1 se o passo não foi observado.</summary>
    public int UltimoTermino(PassoDaInicializacao passo)
    {
        lock (_trava)
        {
            return _anotacoes.FindLastIndex(a => a.Passo == passo && a.Momento == MomentoDoPasso.Terminou);
        }
    }
}
