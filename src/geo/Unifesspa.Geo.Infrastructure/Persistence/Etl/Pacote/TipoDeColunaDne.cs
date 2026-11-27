namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Tipo de uma coluna do pacote dos Correios, no nível exigido pela carga.</summary>
internal enum TipoDeColunaDne
{
    /// <summary>Coluna <c>varchar</c> do arquivo, lida como texto.</summary>
    Texto,

    /// <summary>Coluna <c>int4</c> do arquivo, lida como inteiro de 32 bits.</summary>
    Inteiro,
}
