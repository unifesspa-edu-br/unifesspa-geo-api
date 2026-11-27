namespace Unifesspa.Geo.API.Filters;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Permite um único envio de pacote sendo recebido por vez nesta instância: quem entra segura a
/// permissão até terminar a resposta, e o segundo envio é recusado sem esperar.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via IServiceProvider.AddSingleton<EnvioDoPacoteEmCurso>().")]
internal sealed class EnvioDoPacoteEmCurso : IDisposable
{
    private readonly SemaphoreSlim _permissao = new(1, 1);

    /// <summary>Devolve a permissão a liberar ao fim do envio, ou <see langword="null"/> se já há um envio em curso.</summary>
    public IDisposable? TentarIniciar() => _permissao.Wait(0) ? new Liberacao(_permissao) : null;

    public void Dispose() => _permissao.Dispose();

    private sealed class Liberacao : IDisposable
    {
        private readonly SemaphoreSlim _permissao;
        private int _liberada;

        public Liberacao(SemaphoreSlim permissao)
        {
            _permissao = permissao;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _liberada, 1) == 0)
            {
                _permissao.Release();
            }
        }
    }
}
