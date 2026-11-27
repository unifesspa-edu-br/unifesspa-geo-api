namespace Unifesspa.Geo.IntegrationTests.Correlacao;

using Microsoft.AspNetCore.Http.Features;

/// <summary>
/// Resposta de teste que guarda as ações registradas para o início do envio e as executa quando pedido,
/// como o servidor faz antes de enviar a linha de status.
/// </summary>
internal sealed class CapturaDoInicioDaResposta : HttpResponseFeature
{
    private readonly List<(Func<object, Task> Acao, object Estado)> _acoes = [];

    public override void OnStarting(Func<object, Task> callback, object state)
    {
        _acoes.Add((callback, state));
    }

    public async Task IniciarAsync()
    {
        foreach ((Func<object, Task> acao, object estado) in _acoes)
        {
            await acao(estado);
        }
    }
}
