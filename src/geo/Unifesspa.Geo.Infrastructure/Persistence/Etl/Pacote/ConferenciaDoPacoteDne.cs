namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Conferência do diretório do zip, sem descompactar nada: exatamente as 15 entradas <c>.sql</c>,
/// no máximo uma imagem auxiliar, nenhuma outra entrada, nenhuma cifrada, tamanhos declarados
/// dentro dos limites e uma só versão AAAAMM válida. Todas as faltas voltam juntas.
/// </summary>
internal static class ConferenciaDoPacoteDne
{
    /// <summary>Devolve a versão AAAAMM do pacote ou o erro com todas as faltas encontradas.</summary>
    public static Result<string> Conferir(IReadOnlyList<EntradaDoPacoteDne> entradas)
    {
        ArgumentNullException.ThrowIfNull(entradas);

        List<string> faltas = [];
        ConferirQuantidadeETamanhos(entradas, faltas);
        ContagemDasEntradasDne contagem = new();
        foreach (EntradaDoPacoteDne entrada in entradas)
        {
            contagem.Registrar(entrada);
        }

        contagem.ReportarFaltas(faltas);
        ConferirVersao(contagem.Versoes, faltas);

        return faltas.Count == 0
            ? Result<string>.Success(contagem.Versoes.Single())
            : Result<string>.Failure(ErrosDoPacoteDne.PacoteInvalido(string.Join(' ', faltas)));
    }

    private static void ConferirQuantidadeETamanhos(IReadOnlyList<EntradaDoPacoteDne> entradas, List<string> faltas)
    {
        if (entradas.Count > LimitesDoPacoteDne.EntradasPorPacote)
        {
            faltas.Add($"O pacote tem mais de {LimitesDoPacoteDne.EntradasPorPacote} entradas.");
        }

        if (entradas.Any(entrada => entrada.TamanhoDeclarado > LimitesDoPacoteDne.BytesPorEntrada))
        {
            faltas.Add("Há entrada com tamanho declarado acima de 768 MiB.");
        }

        long total = entradas.Sum(entrada => Math.Clamp(entrada.TamanhoDeclarado, 0, LimitesDoPacoteDne.BytesNoTotal + 1));
        if (total > LimitesDoPacoteDne.BytesNoTotal)
        {
            faltas.Add("O tamanho declarado do conjunto das entradas passa de 1 GiB.");
        }
    }

    private static void ConferirVersao(IReadOnlyCollection<string> versoes, List<string> faltas)
    {
        if (versoes.Count > 1)
        {
            faltas.Add($"As tabelas têm versões diferentes: {string.Join(", ", versoes)}.");
        }
        else if (versoes.Count == 1 && !GeoImportacaoExecucao.EhVersaoValida(versoes.First()))
        {
            faltas.Add("A versão AAAAMM dos nomes das tabelas é inválida (mês deve estar entre 01 e 12).");
        }
    }
}
