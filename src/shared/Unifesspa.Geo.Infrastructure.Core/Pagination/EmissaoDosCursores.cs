namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Application.Abstractions.Authentication;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Emite os cursores das páginas vizinhas de uma resposta paginada. O recurso e o vínculo ao
/// usuário vêm do <see cref="FromCursorAttribute"/> da própria ação, o mesmo que o
/// <see cref="PageRequestModelBinder"/> usa para recusar: um recurso declarado em dois lugares
/// poderia gerar um cursor que a leitura recusa.
/// </summary>
internal sealed class EmissaoDosCursores
{
    private readonly CursorEncoder _codificador;
    private readonly string _recurso;
    private readonly string? _usuarioId;
    private readonly DateTimeOffset _validoAte;

    private EmissaoDosCursores(CursorEncoder codificador, string recurso, string? usuarioId, DateTimeOffset validoAte)
    {
        _codificador = codificador;
        _recurso = recurso;
        _usuarioId = usuarioId;
        _validoAte = validoAte;
    }

    /// <summary>
    /// Resolve o que a emissão precisa. O usuário só é resolvido quando há cursor a emitir e o
    /// recurso exige o vínculo; o instante de validade é lido uma única vez, depois disso, e vale
    /// para os dois cursores.
    /// </summary>
    public static EmissaoDosCursores Para(ControllerBase controller, AncorasDaPagina ancoras)
    {
        FromCursorAttribute atributo = FromCursorAttribute.ExigidoNaAcao(controller.ControllerContext.ActionDescriptor);
        IServiceProvider servicos = controller.HttpContext.RequestServices;
        CursorEncoder codificador = servicos.GetRequiredService<CursorEncoder>();
        CursorPaginationOptions opcoes = servicos.GetRequiredService<IOptions<CursorPaginationOptions>>().Value;
        TimeProvider relogio = servicos.GetRequiredService<TimeProvider>();

        string? usuarioId = ancoras.TemAlguma && atributo.RequireUserBinding ? UsuarioAutenticado(servicos) : null;

        return new EmissaoDosCursores(codificador, atributo.Resource, usuarioId, relogio.GetUtcNow().Add(opcoes.CursorTtl));
    }

    /// <summary>
    /// Cifra o cursor da âncora, vinculado à direção para a qual foi emitido (ADR-0089): a leitura
    /// recusa o reuso com a direção trocada. Sem âncora, não há cursor.
    /// </summary>
    public async Task<string?> EmitirAsync(
        AncoraDoCursor? ancora,
        PaginationDirection direcao,
        int limite,
        CancellationToken cancellationToken)
    {
        if (ancora is not { } ponto)
            return null;

        CursorPayload carga = new(ponto.After, limite, _recurso, _validoAte, direcao, _usuarioId, ponto.SortKey);
        return await _codificador.EncodeAsync(carga, cancellationToken).ConfigureAwait(false);
    }

    // Recurso por usuário (ADR-0026): o cursor leva o usuário corrente e a leitura confere o mesmo
    // vínculo. O contexto do usuário só é resolvido quando há cursor a emitir, para que uma página
    // única não dependa dele.
    private static string UsuarioAutenticado(IServiceProvider servicos)
    {
        IUserContext usuario = servicos.GetRequiredService<IUserContext>();
        if (!usuario.IsAuthenticated || string.IsNullOrEmpty(usuario.UserId))
        {
            throw new InvalidOperationException(
                "OkPaginatedAsync com requireUserBinding=true exige principal autenticado. " +
                "Verifique se o endpoint tem [Authorize] e se o middleware de auth está antes do MVC.");
        }

        return usuario.UserId;
    }
}
