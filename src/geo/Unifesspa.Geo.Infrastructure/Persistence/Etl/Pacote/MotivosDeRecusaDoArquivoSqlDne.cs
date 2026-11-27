namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Motivos fixos das recusas na leitura do arquivo SQL; nenhum inclui conteúdo do arquivo.</summary>
internal static class MotivosDeRecusaDoArquivoSqlDne
{
    public const string ArquivoAcimaDoLimite = "o arquivo descompactado ultrapassa o limite de tamanho.";
    public const string LinhaAcimaDoLimite = "a linha ultrapassa o limite de 64 KiB.";
    public const string CodificacaoInvalida = "o arquivo não está em UTF-8 válido.";
    public const string ComandoForaDaGramatica = "comando fora da gramática aceita neste ponto do arquivo.";
    public const string CodigoAposComentario = "há texto depois do fim do comentário de bloco.";
    public const string NomeDivergente = "o comando cita outra tabela ou outra versão.";
    public const string ColunaInvalida = "coluna fora da gramática aceita ou repetida.";
    public const string ColunasObrigatorias = "faltam colunas obrigatórias ou o tipo de alguma é diferente do esperado.";
    public const string ValoresForaDaGramatica = "valores do INSERT fora da gramática aceita.";
    public const string ValoresIncompativeis = "valores do INSERT não combinam com as colunas da tabela.";
    public const string IndiceInvalido = "índice fora da gramática aceita ou com coluna inexistente.";
    public const string ChavePrimariaInvalida = "chave primária com coluna inexistente.";
    public const string ArquivoIncompleto = "o arquivo termina antes do fim de um comando ou sem a tabela.";
}
