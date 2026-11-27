namespace Unifesspa.Geo.API.Errors;

using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Http;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Core.Errors;

/// <summary>
/// Registry de mapeamentos de erros de domínio do módulo Geo para wire codes /
/// status HTTP. As entidades de localidade (reference data) não têm CRUD de usuário;
/// os mapeamentos são do registro de execução do ETL e do recebimento do pacote.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via IServiceProvider.AddSingleton<IDomainErrorRegistration, GeoDomainErrorRegistration>().")]
internal sealed class GeoDomainErrorRegistration : IDomainErrorRegistration
{
    private static readonly DomainErrorMapping ImportacaoEmAndamento = new(
        StatusCodes.Status409Conflict,
        "uniplus.geo.importacao.em_andamento",
        "Já existe uma importação do Geo em andamento");

    private static readonly DomainErrorMapping ImportacaoNaoEnfileirada = new(
        StatusCodes.Status503ServiceUnavailable,
        "uniplus.geo.importacao.nao_enfileirada",
        "Não foi possível colocar a carga na fila porque o serviço está sendo encerrado");

    private static readonly DomainErrorMapping VersaoNaoProgressiva = new(
        StatusCodes.Status409Conflict,
        "uniplus.geo.importacao.versao_nao_progressiva",
        "A versão do pacote é anterior à última versão aplicada");

    private static readonly DomainErrorMapping PacoteInvalido = new(
        StatusCodes.Status422UnprocessableEntity,
        "uniplus.geo.importacao.pacote_invalido",
        "O pacote enviado não é válido");

    private static readonly DomainErrorMapping PacoteAcimaDoLimite = new(
        StatusCodes.Status413PayloadTooLarge,
        "uniplus.geo.importacao.pacote_acima_do_limite",
        "O pacote enviado é maior que o limite permitido");

    private static readonly DomainErrorMapping TipoDeConteudoNaoSuportado = new(
        StatusCodes.Status415UnsupportedMediaType,
        "uniplus.geo.importacao.tipo_de_conteudo_nao_suportado",
        "O envio deve ser um arquivo zip (application/zip)");

    public IEnumerable<KeyValuePair<string, DomainErrorMapping>> GetMappings() =>
    [
        new(GeoImportacaoErrorCodes.ImportacaoEmAndamento, ImportacaoEmAndamento),
        new(GeoImportacaoErrorCodes.NaoEnfileirada, ImportacaoNaoEnfileirada),
        new(GeoImportacaoErrorCodes.VersaoNaoProgressiva, VersaoNaoProgressiva),
        new(GeoImportacaoErrorCodes.PacoteInvalido, PacoteInvalido),
        new(GeoImportacaoErrorCodes.PacoteAcimaDoLimite, PacoteAcimaDoLimite),
        new(GeoImportacaoErrorCodes.TipoDeConteudoNaoSuportado, TipoDeConteudoNaoSuportado),
    ];
}
