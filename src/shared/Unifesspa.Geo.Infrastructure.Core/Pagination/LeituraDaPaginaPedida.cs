namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

using System.Globalization;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

using Unifesspa.Geo.Application.Abstractions.Authentication;
using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Lê da requisição a página pedida (<c>limit</c>, <c>direction</c> e <c>cursor</c>) ou, quando
/// algum dos três é recusado, registra o código de domínio em
/// <c>HttpContext.Items[CursorBindingErrorCodes.HttpContextItemKey]</c> e a mensagem no
/// <c>ModelState</c>, que o <see cref="CursorPaginationProblemFactory"/> traduz para o status HTTP.
/// </summary>
internal sealed class LeituraDaPaginaPedida
{
    private const string ParametroDoCursor = "cursor";
    private const string ParametroDoLimite = "limit";
    private const string ParametroDaDirecao = "direction";

    private readonly ModelBindingContext _contexto;
    private readonly FromCursorAttribute _atributo;
    private readonly CursorEncoder _codificador;
    private readonly CursorPaginationOptions _opcoes;

    public LeituraDaPaginaPedida(
        ModelBindingContext contexto,
        FromCursorAttribute atributo,
        CursorEncoder codificador,
        CursorPaginationOptions opcoes)
    {
        _contexto = contexto;
        _atributo = atributo;
        _codificador = codificador;
        _opcoes = opcoes;
    }

    /// <summary>
    /// Devolve a página pedida, ou <c>null</c> depois de registrar a recusa. O limite e a direção
    /// são conferidos antes do cursor para não gastar a decifração (AES-GCM) numa consulta que já
    /// seria recusada.
    /// </summary>
    public async Task<PageRequest?> LerOuRecusarAsync()
    {
        IQueryCollection consulta = _contexto.HttpContext.Request.Query;
        if (!TentarLerLimite(consulta[ParametroDoLimite], out int? limitePedido))
        {
            RecusarLimiteForaDoIntervalo();
            return null;
        }
        if (!TentarLerDirecao(consulta[ParametroDaDirecao], out PaginationDirection direcao))
        {
            RecusarDirecaoDesconhecida();
            return null;
        }
        return await LerPaginaAsync(consulta[ParametroDoCursor], direcao, limitePedido).ConfigureAwait(false);
    }

    private async Task<PageRequest?> LerPaginaAsync(string? cursorBruto, PaginationDirection direcao, int? limitePedido) =>
        string.IsNullOrWhiteSpace(cursorBruto)
            ? PrimeiraPagina(limitePedido)
            : await LerPaginaDoCursorAsync(cursorBruto, direcao, limitePedido).ConfigureAwait(false);

    // Sem cursor a página é sempre a primeira e avança ('next'): 'prev' sem âncora não tem
    // sentido e é coagido para 'next' em vez de virar erro.
    private PageRequest PrimeiraPagina(int? limitePedido) =>
        new(AfterId: null, limitePedido ?? _opcoes.LimitDefault, PaginationDirection.Next);

    // O limite da consulta vence o guardado no cursor; o cursor vence o padrão. O que mantém a
    // janela estável é a âncora (keyset), não o limite.
    private async Task<PageRequest?> LerPaginaDoCursorAsync(string cursorBruto, PaginationDirection direcao, int? limitePedido)
    {
        PageRequest? pagina = await LerCursorAsync(cursorBruto, direcao).ConfigureAwait(false);
        return pagina is not null && limitePedido is { } pedido ? pagina with { Limit = pedido } : pagina;
    }

    private bool TentarLerLimite(StringValues bruto, out int? limite)
    {
        limite = LimiteInformado(bruto.FirstOrDefault());
        return limite is not { } pedido || (pedido >= _opcoes.LimitMin && pedido <= _opcoes.LimitMax);
    }

    private static int? LimiteInformado(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        // Texto que não é número vira o menor inteiro, que cai fora do intervalo e é recusado
        // como qualquer outro limite inválido.
        return int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lido)
            ? lido
            : int.MinValue;
    }

    // Direção de navegação: 'next' (padrão, também quando ausente) ou 'prev'; outro valor é recusado.
    private static bool TentarLerDirecao(StringValues bruto, out PaginationDirection direcao)
    {
        string? texto = bruto.FirstOrDefault();
        bool anterior = string.Equals(texto, "prev", StringComparison.OrdinalIgnoreCase);
        direcao = anterior ? PaginationDirection.Prev : PaginationDirection.Next;
        return anterior
            || string.IsNullOrWhiteSpace(texto)
            || string.Equals(texto, "next", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<PageRequest?> LerCursorAsync(string cursorBruto, PaginationDirection direcao)
    {
        CursorDecodeResult decodificado = await _codificador
            .TryDecodeAsync(cursorBruto, _contexto.HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (decodificado.Status != CursorDecodeStatus.Success)
        {
            RecusarPelaDecodificacao(decodificado.Status);
            return null;
        }
        return Aceitar(decodificado.Payload!, direcao);
    }

    private PageRequest? Aceitar(CursorPayload carga, PaginationDirection direcao)
    {
        if (!Guid.TryParse(carga.After, out Guid depoisDe) || !CursorConfere(carga, direcao))
        {
            RecusarComoInvalido();
            return null;
        }

        // O limite do cursor é a memória do tamanho da janela; é ajustado quando a configuração
        // global apertou o intervalo depois da emissão.
        int limite = Math.Clamp(carga.Limit, _opcoes.LimitMin, _opcoes.LimitMax);
        return new PageRequest(depoisDe, limite, direcao, carga.SortKey);
    }

    // Toda divergência é tratada como adulteração e dá a mesma recusa. A ordem importa só porque
    // o usuário corrente é resolvido no fim: antes dele ficam a etiqueta do recurso e a direção
    // (um cursor emitido para 'next' não navega como 'prev', e vice-versa).
    private bool CursorConfere(CursorPayload carga, PaginationDirection direcao) =>
        string.Equals(carga.ResourceTag, _atributo.Resource, StringComparison.Ordinal)
        && carga.Direction == direcao
        && UsuarioConfere(carga)
        && ChaveDeOrdenacaoConfere(carga);

    // Cursor de continuação do keyset ordenado precisa trazer a âncora completa (chave de
    // ordenação e Id): cursor só com Id não pode virar primeira página em silêncio.
    private bool ChaveDeOrdenacaoConfere(CursorPayload carga) =>
        !_atributo.RequireSortKey || !string.IsNullOrWhiteSpace(carga.SortKey);

    // Recurso por usuário: o cursor só vale para quem o recebeu. Anônimo, cursor emitido sem
    // vínculo e cursor de outro usuário dão a mesma recusa, sem revelar qual deles ocorreu. O
    // contexto do usuário só é resolvido quando o recurso exige o vínculo.
    private bool UsuarioConfere(CursorPayload carga)
    {
        if (!_atributo.RequireUserBinding)
            return true;

        IUserContext usuario = _contexto.HttpContext.RequestServices.GetRequiredService<IUserContext>();
        return usuario.IsAuthenticated
            && !string.IsNullOrEmpty(usuario.UserId)
            && string.Equals(carga.UserId, usuario.UserId, StringComparison.Ordinal);
    }

    private void RecusarLimiteForaDoIntervalo() =>
        Recusar(
            CursorBindingErrorCodes.LimitInvalido,
            $"O parâmetro 'limit' deve estar entre {_opcoes.LimitMin} e {_opcoes.LimitMax}.");

    private void RecusarDirecaoDesconhecida() =>
        Recusar(CursorBindingErrorCodes.DirecaoInvalida, "O parâmetro 'direction' deve ser 'next' ou 'prev'.");

    private void RecusarPelaDecodificacao(CursorDecodeStatus status)
    {
        if (status == CursorDecodeStatus.Expired)
            RecusarComoExpirado();
        else
            RecusarComoInvalido();
    }

    private void RecusarComoInvalido() =>
        Recusar(CursorBindingErrorCodes.Invalido, "O cursor informado é inválido.");

    private void RecusarComoExpirado() =>
        Recusar(CursorBindingErrorCodes.Expirado, "O cursor informado expirou.");

    private void Recusar(string codigo, string detalhe)
    {
        _contexto.HttpContext.Items[CursorBindingErrorCodes.HttpContextItemKey] = codigo;
        _contexto.ModelState.AddModelError(_contexto.ModelName, detalhe);
        _contexto.Result = ModelBindingResult.Failed();
    }
}
