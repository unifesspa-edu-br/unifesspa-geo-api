namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// Comando SQL do esquema de carga já conferido. Só <see cref="ComandosDoEsquemaDeCarga"/> o cria;
/// quem executa comandos do esquema recebe este tipo, nunca um texto solto.
/// </summary>
internal sealed class ComandoDoEsquemaDeCarga
{
    internal ComandoDoEsquemaDeCarga(string texto)
    {
        Texto = texto;
    }

    public string Texto { get; }
}
