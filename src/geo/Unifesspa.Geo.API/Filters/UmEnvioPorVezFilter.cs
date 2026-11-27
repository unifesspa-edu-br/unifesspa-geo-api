namespace Unifesspa.Geo.API.Filters;

using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Mvc.Filters;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Core.Errors;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Recusa com 409, antes de ler o corpo, o envio que chega enquanto outro está sendo recebido nesta
/// instância. Segura a permissão do envio até o fim da resposta, em qualquer desfecho. A carga em
/// andamento e a trava da carga são conferidas pelo registro das importações.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via IServiceProvider pelo [ServiceFilter] do controller.")]
internal sealed class UmEnvioPorVezFilter : IAsyncResourceFilter
{
    private readonly EnvioDoPacoteEmCurso _emCurso;
    private readonly IDomainErrorMapper _mapper;

    public UmEnvioPorVezFilter(EnvioDoPacoteEmCurso emCurso, IDomainErrorMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(emCurso);
        ArgumentNullException.ThrowIfNull(mapper);
        _emCurso = emCurso;
        _mapper = mapper;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        using IDisposable? permissao = _emCurso.TentarIniciar();
        if (permissao is null)
        {
            context.Result = Result.Failure(ErrosDoPacoteDne.EmAndamento()).ToActionResult(_mapper);
            return;
        }

        await next().ConfigureAwait(false);
    }
}
