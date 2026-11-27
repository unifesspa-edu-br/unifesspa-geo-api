namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Binder de <see cref="PageRequest"/>: lê <c>cursor</c> e <c>limit</c> do
/// query string, valida limit contra <see cref="CursorPaginationOptions"/>,
/// decoda o cursor opaco (AES-GCM via <see cref="CursorEncoder"/>, ADR-0026)
/// e produz um <see cref="PageRequest"/> tipado para a action.
/// <para>
/// Erros de parsing publicam o código de domínio em
/// <c>HttpContext.Items[CursorBindingErrorCodes.HttpContextItemKey]</c> e
/// adicionam mensagem ao <c>ModelState</c>; o
/// <see cref="CursorPaginationProblemFactory"/> traduz para o HTTP status
/// correto (400/410/422) via <c>IDomainErrorMapper</c>.
/// </para>
/// </summary>
public sealed class PageRequestModelBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        PageRequest? pagina = await CriarLeitura(bindingContext).LerOuRecusarAsync().ConfigureAwait(false);
        if (pagina is not null)
            bindingContext.Result = ModelBindingResult.Success(pagina);
    }

    private static LeituraDaPaginaPedida CriarLeitura(ModelBindingContext bindingContext)
    {
        FromCursorAttribute atributo = FromCursorAttribute.DaAcao(bindingContext.ActionContext.ActionDescriptor)
            ?? throw new InvalidOperationException(
                $"Parâmetro do tipo {nameof(PageRequest)} requer [FromCursor(\"<recurso>\")].");

        IServiceProvider servicos = bindingContext.HttpContext.RequestServices;
        CursorEncoder codificador = servicos.GetRequiredService<CursorEncoder>();
        CursorPaginationOptions opcoes = servicos.GetRequiredService<IOptions<CursorPaginationOptions>>().Value;

        return new LeituraDaPaginaPedida(bindingContext, atributo, codificador, opcoes);
    }
}
