namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

using System.Reflection;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;

/// <summary>
/// Sinaliza que um parâmetro <see cref="PageRequest"/> da action deve ser
/// preenchido pelo <see cref="PageRequestModelBinder"/>: lê <c>cursor</c> e
/// <c>limit</c> do query string, decoda o cursor opaco (ADR-0026), valida
/// o <see cref="Resource"/> contra o <c>ResourceTag</c> embutido no payload e
/// publica o <see cref="PageRequest"/> resultante para a action consumir.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FromCursorAttribute : ModelBinderAttribute
{
    public FromCursorAttribute(string resource)
        : base(typeof(PageRequestModelBinder))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        Resource = resource;
        BindingSource = Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Query;
    }

    /// <summary>Identificador do recurso embutido no payload do cursor (ex.: <c>"editais"</c>).</summary>
    public string Resource { get; }

    /// <summary>
    /// Quando <c>true</c>, o binder popula <see cref="CursorPayload.UserId"/>
    /// com o sub claim do principal autenticado na emissão do cursor e valida
    /// igualdade no decode — cursor de Alice não é navegável por Bob mesmo
    /// que decoda corretamente (gap LGPD em metadata de recursos user-scoped,
    /// ADR-0026). Default <c>false</c> (recurso público).
    /// </summary>
    public bool RequireUserBinding { get; init; }

    /// <summary>
    /// Quando <c>true</c>, cursores de continuação precisam trazer
    /// <see cref="CursorPayload.SortKey"/>. Use em recursos com keyset ordenado
    /// multi-coluna, nos quais a âncora é o par <c>(SortKey, Id)</c>.
    /// </summary>
    public bool RequireSortKey { get; init; }

    /// <summary>
    /// Localiza o atributo do parâmetro <see cref="PageRequest"/> da ação, ou <c>null</c> quando
    /// a ação não o declara. A leitura, a emissão e o OpenAPI partem deste mesmo ponto.
    /// </summary>
    internal static FromCursorAttribute? DaAcao(ActionDescriptor descritor) =>
        // ParameterDescriptor.BindingInfo expõe o BinderType, mas não o atributo em si. O caminho
        // confiável é a ParameterInfo do ControllerParameterDescriptor, que também resiste a renomes
        // do parâmetro.
        (descritor as ControllerActionDescriptor)?.Parameters
            .OfType<ControllerParameterDescriptor>()
            .Where(static parametro => parametro.ParameterType == typeof(PageRequest))
            .Select(static parametro => parametro.ParameterInfo.GetCustomAttribute<FromCursorAttribute>())
            .FirstOrDefault(static atributo => atributo is not null);

    /// <summary>
    /// Como <see cref="DaAcao"/>, mas falha quando a ação não declara o atributo: ler ou emitir
    /// cursor sem saber o recurso não tem como continuar.
    /// </summary>
    internal static FromCursorAttribute ExigidoNaAcao(ActionDescriptor descritor) =>
        DaAcao(descritor)
            ?? throw new InvalidOperationException(
                $"Parâmetro do tipo {nameof(PageRequest)} requer [FromCursor(\"<recurso>\")].");
}
