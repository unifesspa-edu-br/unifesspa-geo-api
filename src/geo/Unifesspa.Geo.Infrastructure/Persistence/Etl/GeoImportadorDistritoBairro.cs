namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;
using Unifesspa.Geo.Kernel.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

using static Unifesspa.Geo.Infrastructure.Persistence.Etl.RegrasDaCargaPorChaveNatural;

/// <summary>
/// Mapas de resolução de FK construídos pela carga de Distrito/Bairro: traduzem os ids
/// int4 da fonte DNE (instáveis entre versões) para os Guid intra-banco (ADR-0054),
/// consumidos pela carga de logradouros. <c>id_distrito</c>/<c>id_bairro</c> são as PKs
/// da fonte (únicos dentro de uma versão); homônimos em cidades distintas resolvem
/// para Guids diferentes porque cada par <c>(cidade, nome)</c> é uma entidade própria.
/// </summary>
internal sealed record ResolucaoLocalidades(
    Dictionary<int, Guid> CidadesPorIdDne,
    Dictionary<int, LocalidadeResolvida> DistritosPorIdDne,
    Dictionary<int, LocalidadeResolvida> BairrosPorIdDne);

/// <summary>
/// Distrito/Bairro resolvido: o Guid intra-banco e a <see cref="CidadeId"/> a que
/// pertence. O logradouro só adota a FK de distrito/bairro se a cidade resolvida bater
/// com a sua — garantindo a coerência hierárquica que o domínio delega à carga.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LocalidadeResolvida(Guid Id, Guid CidadeId);

/// <summary>
/// Carga das folhas de volume modesto por gravação por chave natural (idempotente):
/// Distrito e Bairro (+faixas), CEP de grande usuário e CEP geral.
/// Executa dentro da transação ativa do <see cref="GeoDbContext"/> e devolve a
/// <see cref="ResolucaoLocalidades"/> (dicionários de FK) para a carga em lote dos
/// logradouros.
/// </summary>
internal sealed class GeoImportadorDistritoBairro
{
    private readonly GeoDbContext _contexto;

    public GeoImportadorDistritoBairro(GeoDbContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        _contexto = contexto;
    }

    public async Task<ResolucaoLocalidades> ImportarAsync(
        IGeoFonteDados fonte,
        RelatorioImportacao relatorio,
        CancellationToken cancellationToken)
    {
        ProvenienciaDaCarga proveniencia = new(fonte.Versao);
        Dictionary<int, Guid> cidadesPorIdDne =
            await ResolverCidadesPorIdDneAsync(fonte, cancellationToken).ConfigureAwait(false);
        Dictionary<int, LocalidadeResolvida> distritosPorIdDne =
            await ImportarDistritosAsync(fonte, cidadesPorIdDne, proveniencia, relatorio, cancellationToken).ConfigureAwait(false);
        await ImportarFaixasDeDistritoAsync(fonte, distritosPorIdDne, proveniencia, relatorio, cancellationToken).ConfigureAwait(false);
        Dictionary<int, LocalidadeResolvida> bairrosPorIdDne =
            await ImportarBairrosAsync(fonte, cidadesPorIdDne, proveniencia, relatorio, cancellationToken).ConfigureAwait(false);
        await ImportarFaixasDeBairroAsync(fonte, bairrosPorIdDne, proveniencia, relatorio, cancellationToken).ConfigureAwait(false);
        await ImportarGrandesUsuariosAsync(fonte, proveniencia, relatorio, cancellationToken).ConfigureAwait(false);
        await ImportarCepGeraisAsync(fonte, cidadesPorIdDne, distritosPorIdDne, proveniencia, relatorio, cancellationToken).ConfigureAwait(false);
        return new ResolucaoLocalidades(cidadesPorIdDne, distritosPorIdDne, bairrosPorIdDne);
    }

    private async Task<Dictionary<int, LocalidadeResolvida>> ImportarDistritosAsync(
        IGeoFonteDados fonte,
        Dictionary<int, Guid> cidadesPorIdDne,
        ProvenienciaDaCarga proveniencia,
        RelatorioImportacao relatorio,
        CancellationToken cancellationToken)
    {
        GravacaoPorChaveNatural<Distrito> gravacao = await GravacaoPorChaveNatural<Distrito>.AbrirAsync(
            _contexto.Distritos, d => LocalidadeParaCarga.ChaveDe(d.CidadeId, d.NomeNormalizado), relatorio.Tabela("distrito"), cancellationToken).ConfigureAwait(false);
        return await ImportarLocalidadesAsync(
            ComoLocalidades(
                fonte.LerDistritosAsync(cancellationToken),
                static d => new LocalidadeDaFonte(d.IdDistrito, d.Nome, d.NomeSemAcento, d.CidadeIdDne, d.Uf, d.Latitude, d.Longitude),
                cancellationToken),
            cidadesPorIdDne,
            proveniencia,
            gravacao,
            static l => l.CriarDistrito(),
            static (e, l) => l.Atualizar(e),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ImportarFaixasDeDistritoAsync(
        IGeoFonteDados fonte,
        Dictionary<int, LocalidadeResolvida> distritosPorIdDne,
        ProvenienciaDaCarga proveniencia,
        RelatorioImportacao relatorio,
        CancellationToken cancellationToken)
    {
        GravacaoPorChaveNatural<DistritoFaixaCep> gravacao = await GravacaoPorChaveNatural<DistritoFaixaCep>.AbrirAsync(
            _contexto.DistritoFaixasCep,
            f => ChaveFaixa(f.DistritoId, f.CepInicial, f.CepFinal),
            relatorio.Tabela("distrito_faixa"),
            cancellationToken).ConfigureAwait(false);
        await ImportarFaixasAsync(
            fonte.LerDistritoFaixasAsync(cancellationToken),
            distritosPorIdDne,
            proveniencia,
            gravacao,
            static f => f.CriarFaixaDeDistrito(),
            static (e, f) => f.Atualizar(e),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<int, LocalidadeResolvida>> ImportarBairrosAsync(
        IGeoFonteDados fonte,
        Dictionary<int, Guid> cidadesPorIdDne,
        ProvenienciaDaCarga proveniencia,
        RelatorioImportacao relatorio,
        CancellationToken cancellationToken)
    {
        GravacaoPorChaveNatural<Bairro> gravacao = await GravacaoPorChaveNatural<Bairro>.AbrirAsync(
            _contexto.Bairros, b => LocalidadeParaCarga.ChaveDe(b.CidadeId, b.NomeNormalizado), relatorio.Tabela("bairro"), cancellationToken).ConfigureAwait(false);
        return await ImportarLocalidadesAsync(
            ComoLocalidades(
                fonte.LerBairrosAsync(cancellationToken),
                static b => new LocalidadeDaFonte(b.IdBairro, b.Nome, b.NomeSemAcento, b.CidadeIdDne, b.Uf, b.Latitude, b.Longitude),
                cancellationToken),
            cidadesPorIdDne,
            proveniencia,
            gravacao,
            static l => l.CriarBairro(),
            static (e, l) => l.Atualizar(e),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ImportarFaixasDeBairroAsync(
        IGeoFonteDados fonte,
        Dictionary<int, LocalidadeResolvida> bairrosPorIdDne,
        ProvenienciaDaCarga proveniencia,
        RelatorioImportacao relatorio,
        CancellationToken cancellationToken)
    {
        GravacaoPorChaveNatural<BairroFaixaCep> gravacao = await GravacaoPorChaveNatural<BairroFaixaCep>.AbrirAsync(
            _contexto.BairroFaixasCep,
            f => ChaveFaixa(f.BairroId, f.CepInicial, f.CepFinal),
            relatorio.Tabela("bairro_faixa"),
            cancellationToken).ConfigureAwait(false);
        await ImportarFaixasAsync(
            fonte.LerBairroFaixasAsync(cancellationToken),
            bairrosPorIdDne,
            proveniencia,
            gravacao,
            static f => f.CriarFaixaDeBairro(),
            static (e, f) => f.Atualizar(e),
            cancellationToken).ConfigureAwait(false);
    }

    // {id_cidade (inteiro da fonte) → Cidade.Id} pela junção da tabela de carga
    // (id_cidade→cidade_ibge) com o domínio (codigo_ibge→Id). A Cidade não guarda o
    // identificador inteiro da DNE (chave natural = codigo_ibge), então a ponte é o código IBGE. Cidades não carregadas no domínio são ignoradas.
    private async Task<Dictionary<int, Guid>> ResolverCidadesPorIdDneAsync(
        IGeoFonteDados fonte,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Guid> ibgeParaGuid =
            await _contexto.Cidades.ToDictionaryAsync(c => c.CodigoIbge, c => c.Id, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);

        return await ResolverIdsDneParaGuidAsync(
            fonte.LerCidadeIdsAsync(cancellationToken),
            cru => cru.IdCidade,
            cru => cru.CodigoIbge,
            ibgeParaGuid).ConfigureAwait(false);
    }

    private async Task<Dictionary<int, LocalidadeResolvida>> ImportarLocalidadesAsync<T>(
        IAsyncEnumerable<LocalidadeDaFonte> linhas,
        Dictionary<int, Guid> cidadesPorIdDne,
        ProvenienciaDaCarga proveniencia,
        GravacaoPorChaveNatural<T> gravacao,
        Func<LocalidadeParaCarga, Result<T>> criar,
        Func<T, LocalidadeParaCarga, Result> atualizar,
        CancellationToken cancellationToken)
        where T : EntityBase
    {
        Dictionary<int, LocalidadeResolvida> idDneParaGuid = [];
        await foreach (LocalidadeDaFonte linha in linhas.ConfigureAwait(false))
        {
            gravacao.Contador.ContarLido();
            if (LocalidadeParaCarga.De(linha, cidadesPorIdDne, proveniencia, gravacao.Contador) is { } dados)
            {
                T? entidade = gravacao.Gravar(dados.Chave, () => criar(dados), e => atualizar(e, dados));
                RegistrarResolvida(idDneParaGuid, linha.IdDne, entidade?.Id, dados.CidadeId);
            }
        }

        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return idDneParaGuid;
    }

    // O Id é gerado no cliente, então já vale antes do SaveChanges.
    private static void RegistrarResolvida(
        Dictionary<int, LocalidadeResolvida> idDneParaGuid, int? idDne, Guid? id, Guid cidadeId)
    {
        if (idDne is int idDaFonte && id is Guid idGravado)
        {
            idDneParaGuid[idDaFonte] = new LocalidadeResolvida(idGravado, cidadeId);
        }
    }

    private async Task ImportarFaixasAsync<T>(
        IAsyncEnumerable<FaixaLocalidadeCru> linhas,
        Dictionary<int, LocalidadeResolvida> localidadesPorIdDne,
        ProvenienciaDaCarga proveniencia,
        GravacaoPorChaveNatural<T> gravacao,
        Func<FaixaParaCarga, Result<T>> criar,
        Func<T, FaixaParaCarga, Result> atualizar,
        CancellationToken cancellationToken)
        where T : EntityBase
    {
        await foreach (FaixaLocalidadeCru linha in linhas.ConfigureAwait(false))
        {
            gravacao.Contador.ContarLido();
            if (FaixaParaCarga.De(linha, localidadesPorIdDne, proveniencia, gravacao.Contador) is { } dados)
            {
                gravacao.Gravar(dados.Chave, () => criar(dados), e => atualizar(e, dados));
            }
        }

        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ImportarGrandesUsuariosAsync(
        IGeoFonteDados fonte,
        ProvenienciaDaCarga proveniencia,
        RelatorioImportacao relatorio,
        CancellationToken cancellationToken)
    {
        GravacaoPorChaveNatural<CepGrandeUsuario> gravacao = await GravacaoPorChaveNatural<CepGrandeUsuario>.AbrirAsync(
            _contexto.CepGrandesUsuarios, g => g.Cep, relatorio.Tabela("cep_grande_usuario"), cancellationToken).ConfigureAwait(false);

        await foreach (CepGrandeUsuarioCru linha in fonte.LerCepGrandesUsuariosAsync(cancellationToken).ConfigureAwait(false))
        {
            gravacao.Contador.ContarLido();
            if (CepGrandeUsuarioParaCarga.De(linha, proveniencia, gravacao.Contador) is { } dados)
            {
                gravacao.Gravar(dados.Cep, dados.Criar, dados.Atualizar);
            }
        }

        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // CEP geral de cidade/distrito (linha de logradouro sem nome na fonte). Chave natural =
    // CEP; reexecutar atualiza o registro existente.
    private async Task ImportarCepGeraisAsync(
        IGeoFonteDados fonte,
        Dictionary<int, Guid> cidadesPorIdDne,
        Dictionary<int, LocalidadeResolvida> distritosPorIdDne,
        ProvenienciaDaCarga proveniencia,
        RelatorioImportacao relatorio,
        CancellationToken cancellationToken)
    {
        GravacaoPorChaveNatural<CepGeral> gravacao = await GravacaoPorChaveNatural<CepGeral>.AbrirAsync(
            _contexto.CepGerais, g => g.Cep, relatorio.Tabela("cep_geral"), cancellationToken).ConfigureAwait(false);

        await foreach (CepGeralCru linha in fonte.LerCepGeraisAsync(cancellationToken).ConfigureAwait(false))
        {
            gravacao.Contador.ContarLido();
            if (CepGeralParaCarga.De(linha, cidadesPorIdDne, distritosPorIdDne, proveniencia, gravacao.Contador) is { } dados)
            {
                gravacao.Gravar(dados.Cep, dados.Criar, dados.Atualizar);
            }
        }

        await _contexto.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<LocalidadeDaFonte> ComoLocalidades<TFonte>(
        IAsyncEnumerable<TFonte> fonte,
        Func<TFonte, LocalidadeDaFonte> projetar,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (TFonte item in fonte.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return projetar(item);
        }
    }
}
