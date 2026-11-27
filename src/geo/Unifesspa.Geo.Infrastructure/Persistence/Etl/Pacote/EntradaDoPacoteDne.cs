namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Entrada do diretório do zip, como o arquivo a declara (nada foi descompactado).</summary>
internal sealed record EntradaDoPacoteDne(string Nome, long TamanhoDeclarado, bool Cifrada);
