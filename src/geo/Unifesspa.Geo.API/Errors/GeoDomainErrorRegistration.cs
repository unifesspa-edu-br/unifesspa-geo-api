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
    public IEnumerable<KeyValuePair<string, DomainErrorMapping>> GetMappings() =>
    [
        new(GeoImportacaoErrorCodes.VersaoFormatoInvalido,
            new DomainErrorMapping(
                StatusCodes.Status422UnprocessableEntity,
                "uniplus.geo.importacao.versao_formato_invalido",
                "Versão da base fora do formato AAAAMM")),

        new(GeoImportacaoErrorCodes.DisparadoPorObrigatorio,
            new DomainErrorMapping(
                StatusCodes.Status422UnprocessableEntity,
                "uniplus.geo.importacao.disparado_por_obrigatorio",
                "A identificação de quem disparou a carga é obrigatória")),

        new(GeoImportacaoErrorCodes.ImportacaoEmAndamento,
            new DomainErrorMapping(
                StatusCodes.Status409Conflict,
                "uniplus.geo.importacao.em_andamento",
                "Já existe uma importação do Geo em andamento")),

        new(GeoImportacaoErrorCodes.NaoEnfileirada,
            new DomainErrorMapping(
                StatusCodes.Status503ServiceUnavailable,
                "uniplus.geo.importacao.nao_enfileirada",
                "Não foi possível colocar a carga na fila porque o serviço está sendo encerrado")),

        new(GeoImportacaoErrorCodes.VersaoNaoProgressiva,
            new DomainErrorMapping(
                StatusCodes.Status409Conflict,
                "uniplus.geo.importacao.versao_nao_progressiva",
                "A versão do pacote é anterior à última versão aplicada")),

        new(GeoImportacaoErrorCodes.PacoteInvalido,
            new DomainErrorMapping(
                StatusCodes.Status422UnprocessableEntity,
                "uniplus.geo.importacao.pacote_invalido",
                "O pacote enviado não é válido")),

        new(GeoImportacaoErrorCodes.PacoteAcimaDoLimite,
            new DomainErrorMapping(
                StatusCodes.Status413PayloadTooLarge,
                "uniplus.geo.importacao.pacote_acima_do_limite",
                "O pacote enviado é maior que o limite permitido")),

        new(GeoImportacaoErrorCodes.TipoDeConteudoNaoSuportado,
            new DomainErrorMapping(
                StatusCodes.Status415UnsupportedMediaType,
                "uniplus.geo.importacao.tipo_de_conteudo_nao_suportado",
                "O envio deve ser um arquivo zip (application/zip)")),
    ];
}
