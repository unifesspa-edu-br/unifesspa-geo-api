namespace Unifesspa.Geo.API.Errors;

using System.Globalization;

using Microsoft.AspNetCore.Http;

using Unifesspa.Geo.API.Formatting;
using Unifesspa.Geo.Infrastructure.Core.Errors;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Recusas das consultas de CEP, cidade, estado e busca. O detalhe de cada função é fixo:
/// nunca repete o valor recebido.
/// </summary>
internal static class ErrosDasConsultas
{
    public static readonly DomainErrorMapping CepFormatoInvalido = new(
        StatusCodes.Status400BadRequest, "uniplus.geo.cep.formato_invalido", "CEP fora do formato");

    public static readonly DomainErrorMapping CepNaoEncontrado = new(
        StatusCodes.Status404NotFound, "uniplus.geo.cep.nao_encontrado", "CEP não encontrado");

    public static readonly DomainErrorMapping CodigoIbgeInvalido = new(
        StatusCodes.Status400BadRequest, "uniplus.geo.cidade.codigo_ibge_invalido", "Código IBGE fora do formato");

    public static readonly DomainErrorMapping CidadeNaoEncontrada = new(
        StatusCodes.Status404NotFound, "uniplus.geo.cidade.nao_encontrada", "Cidade não encontrada");

    public static readonly DomainErrorMapping UfInvalida = new(
        StatusCodes.Status400BadRequest, "uniplus.geo.estado.uf_invalida", "UF fora do formato");

    public static readonly DomainErrorMapping EstadoNaoEncontrado = new(
        StatusCodes.Status404NotFound, "uniplus.geo.estado.nao_encontrado", "Estado não encontrado");

    public static readonly DomainErrorMapping TermoDeBuscaMuitoLongo = new(
        StatusCodes.Status400BadRequest, "uniplus.geo.busca.termo_muito_longo", "Termo de busca longo demais");

    public static readonly DomainErrorMapping ParametrosDeProximidadeAusentes = new(
        StatusCodes.Status400BadRequest, "uniplus.geo.proximidade.parametros_ausentes", "Parâmetros da consulta por proximidade ausentes");

    public static readonly DomainErrorMapping LatitudeInvalida = new(
        StatusCodes.Status400BadRequest, "uniplus.geo.proximidade.latitude_invalida", "Latitude fora da faixa");

    public static readonly DomainErrorMapping LongitudeInvalida = new(
        StatusCodes.Status400BadRequest, "uniplus.geo.proximidade.longitude_invalida", "Longitude fora da faixa");

    public static readonly DomainErrorMapping RaioInvalido = new(
        StatusCodes.Status400BadRequest, "uniplus.geo.proximidade.raio_invalido", "Raio fora da faixa");

    public static readonly DomainErrorMapping LimiteInvalido = new(
        StatusCodes.Status400BadRequest, "uniplus.geo.proximidade.limite_invalido", "Limite de resultados inválido");

    public static readonly DomainErrorMapping ImportacaoNaoEncontrada = new(
        StatusCodes.Status404NotFound, "uniplus.geo.importacao.nao_encontrada", "Importação não encontrada");

    public static DomainError ErroCepFormatoInvalido() => new(
        CepFormatoInvalido.Code, "O CEP deve ter exatamente 8 dígitos (ex.: 01001000 ou 01001-000).");

    public static DomainError ErroCepNaoEncontrado() => new(
        CepNaoEncontrado.Code, "Não há endereço cadastrado para o CEP informado.");

    public static DomainError ErroCodigoIbgeInvalido() => new(
        CodigoIbgeInvalido.Code, "O código IBGE deve ter exatamente 7 dígitos.");

    public static DomainError ErroCidadeNaoEncontrada() => new(
        CidadeNaoEncontrada.Code, "Não há cidade cadastrada para o código IBGE informado.");

    public static DomainError ErroUfInvalida() => new(
        UfInvalida.Code, "A UF deve ter exatamente 2 letras (ex.: PA).");

    public static DomainError ErroEstadoNaoEncontrado() => new(
        EstadoNaoEncontrado.Code, "Não há estado cadastrado para a UF informada.");

    public static DomainError ErroTermoDeBuscaMuitoLongo() => new(
        TermoDeBuscaMuitoLongo.Code, $"O parâmetro 'q' não pode exceder {TermoDeBusca.TamanhoMaximo} caracteres.");

    public static DomainError ErroParametrosDeProximidadeAusentes() => new(
        ParametrosDeProximidadeAusentes.Code, "Informe os parâmetros 'lat', 'long' e 'raioKm'.");

    public static DomainError ErroLatitudeInvalida() => new(
        LatitudeInvalida.Code, "A latitude deve estar entre -90 e 90 graus.");

    public static DomainError ErroLongitudeInvalida() => new(
        LongitudeInvalida.Code, "A longitude deve estar entre -180 e 180 graus.");

    public static DomainError ErroRaioInvalido(double raioMaxKm) => new(
        RaioInvalido.Code,
        string.Create(
            CultureInfo.InvariantCulture,
            $"O raio (raioKm) deve ser maior que 0 e no máximo {raioMaxKm} km."));

    public static DomainError ErroLimiteInvalido() => new(
        LimiteInvalido.Code, "O parâmetro 'limit' deve ser maior que 0.");

    public static DomainError ErroImportacaoNaoEncontrada() => new(
        ImportacaoNaoEncontrada.Code, "Não há importação cadastrada para o identificador informado.");
}
