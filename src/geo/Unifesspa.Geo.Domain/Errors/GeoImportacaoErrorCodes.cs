namespace Unifesspa.Geo.Domain.Errors;

/// <summary>
/// Códigos de erro de domínio do registro de execução do ETL DNE.
/// Seguem o padrão <c>{Entidade}.{Falha}</c> dos demais módulos; o mapeamento para
/// status HTTP / wire code vive em <c>Geo.API/Errors/GeoDomainErrorRegistration</c>.
/// </summary>
public static class GeoImportacaoErrorCodes
{
    /// <summary>Versão fora do formato AAAAMM (6 dígitos, mês 01–12).</summary>
    public const string VersaoFormatoInvalido = "GeoImportacao.VersaoFormatoInvalido";

    /// <summary>Identificação de quem disparou a carga ausente.</summary>
    public const string DisparadoPorObrigatorio = "GeoImportacao.DisparadoPorObrigatorio";

    /// <summary>Já existe uma carga em andamento — uma execução por vez (concorrência → 409).</summary>
    public const string ImportacaoEmAndamento = "GeoImportacao.ImportacaoEmAndamento";

    /// <summary>A carga foi registrada mas não pôde ser enfileirada (serviço sendo encerrado → 503).</summary>
    public const string NaoEnfileirada = "GeoImportacao.NaoEnfileirada";

    /// <summary>Versão anterior à última já aplicada — aplicar rebaixaria os dados (→ 409).</summary>
    public const string VersaoNaoProgressiva = "GeoImportacao.VersaoNaoProgressiva";

    /// <summary>Transição de estado inválida (ex.: concluir uma execução que não está em andamento).</summary>
    public const string TransicaoInvalida = "GeoImportacao.TransicaoInvalida";

    /// <summary>O pacote enviado não cumpre a conferência do diretório do zip (→ 422).</summary>
    public const string PacoteInvalido = "GeoImportacao.PacoteInvalido";

    /// <summary>O pacote enviado ultrapassa o tamanho máximo configurado (→ 413).</summary>
    public const string PacoteAcimaDoLimite = "GeoImportacao.PacoteAcimaDoLimite";

    /// <summary>O envio não tem o tipo de conteúdo <c>application/zip</c> (→ 415).</summary>
    public const string TipoDeConteudoNaoSuportado = "GeoImportacao.TipoDeConteudoNaoSuportado";
}
