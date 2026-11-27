namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Npgsql;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// Extrai metadados seguros de uma exceção para registro em log, omitindo
/// <see cref="Exception.Message"/>, detalhes livres do provedor e rastreamento
/// de pilha que possam conter valores da fonte ou dados sensíveis.
/// </summary>
internal static class DiagnosticoDeExcecaoSanitizado
{
    private const int ProfundidadeMaxima = 16;

    public static string Descrever(Exception excecao)
    {
        ArgumentNullException.ThrowIfNull(excecao);

        string tipo = NomeDoTipo(excecao);
        string? tiposInternos = ExtrairTiposInternos(excecao.InnerException);
        PostgresException? pg = EncontrarPostgresException(excecao);

        return Formatar(tipo, tiposInternos, pg) + ComplementoDaLeituraDoPacote(excecao);
    }

    // Tabela (da lista fechada) e número da linha da recusa do pacote; nunca o conteúdo do arquivo.
    private static string ComplementoDaLeituraDoPacote(Exception excecao) =>
        excecao is ErroDeLeituraDoPacoteDneException leitura
            ? $"; TabelaDoPacote={leitura.Tabela}; Linha={leitura.Linha}"
            : string.Empty;

    private static string Formatar(string tipo, string? tiposInternos, PostgresException? pg)
    {
        string baseTexto = tiposInternos is null
            ? $"Tipo={tipo}"
            : $"Tipo={tipo}; TiposInternos={tiposInternos}";

        if (pg is null)
        {
            return baseTexto;
        }

        string tabela = string.IsNullOrWhiteSpace(pg.TableName) ? "-" : pg.TableName;
        string restricao = string.IsNullOrWhiteSpace(pg.ConstraintName) ? "-" : pg.ConstraintName;
        return $"{baseTexto}; SqlState={pg.SqlState}; Tabela={tabela}; Restricao={restricao}";
    }

    private static string? ExtrairTiposInternos(Exception? inicial)
    {
        if (inicial is null)
        {
            return null;
        }

        List<string> tipos = [];
        Exception? atual = inicial;
        for (int i = 0; atual is not null && i < ProfundidadeMaxima; i++)
        {
            tipos.Add(NomeDoTipo(atual));
            atual = atual.InnerException;
        }

        return string.Join(" -> ", tipos);
    }

    private static PostgresException? EncontrarPostgresException(Exception inicial)
    {
        Exception? atual = inicial;
        for (int i = 0; atual is not null && i < ProfundidadeMaxima; i++)
        {
            if (atual is PostgresException pg)
            {
                return pg;
            }

            atual = atual.InnerException;
        }

        return null;
    }

    private static string NomeDoTipo(Exception excecao) =>
        excecao.GetType().FullName ?? excecao.GetType().Name;
}
