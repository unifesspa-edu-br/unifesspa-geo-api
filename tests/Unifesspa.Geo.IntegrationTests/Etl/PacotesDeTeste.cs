namespace Unifesspa.Geo.IntegrationTests.Etl;

using Unifesspa.Geo.Domain.Entities;

/// <summary>Pacotes recebidos fictícios; o resumo SHA-256 é o caractere informado repetido 64 vezes.</summary>
internal static class PacotesDeTeste
{
    private const int TamanhoDoResumo = 64;

    public static PacoteRecebido Recebido(string versao, char resumo = 'a', long tamanhoEmBytes = 2048) =>
        new(versao, new string(resumo, TamanhoDoResumo), tamanhoEmBytes);
}
