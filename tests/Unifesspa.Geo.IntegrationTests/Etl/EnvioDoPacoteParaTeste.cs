namespace Unifesspa.Geo.IntegrationTests.Etl;

using System.Security.Cryptography;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Admin;
using Unifesspa.Geo.IntegrationTests.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Diretório de pacotes próprio do teste: monta o registro das importações sobre ele, fornece zips
/// fictícios com conteúdo estável (o mesmo versão e variante sempre dão o mesmo resumo SHA-256) e
/// permite conferir quais arquivos restaram.
/// </summary>
internal sealed class EnvioDoPacoteParaTeste : IDisposable
{
    private readonly string _diretorio = Path.Combine(Path.GetTempPath(), $"pacotes-dne-{Guid.NewGuid():N}");
    private readonly Dictionary<(string Versao, char Variante), ZipDeTeste> _zips = [];

    public EnvioDoPacoteParaTeste()
    {
        Arquivos = new ArquivoDoPacoteDne(_diretorio);
    }

    public ArquivoDoPacoteDne Arquivos { get; }

    /// <summary>Fonte usada no encerramento das cargas abandonadas; por padrão, uma sem esquemas a descartar.</summary>
    public IFonteDaExecucaoDne Fonte { get; set; } = new FabricaDeFonteFixa();

    /// <summary>Nomes dos arquivos que restaram no diretório de pacotes.</summary>
    public IReadOnlyList<string> NomesDosArquivos() =>
        Directory.Exists(_diretorio) ? [.. Directory.EnumerateFiles(_diretorio).Select(Path.GetFileName).OfType<string>()] : [];

    public string Diretorio => _diretorio;

    /// <summary>Zip fictício da versão; a variante muda o conteúdo e, portanto, o resumo SHA-256.</summary>
    public ZipDeTeste Receber(string versao, char variante = 'a')
    {
        if (!_zips.TryGetValue((versao, variante), out ZipDeTeste? zip))
        {
            byte[] bytes = PacoteSinteticoDne.Completo(versao)
                .Com("paises_ibge", ("sigla_iso", variante.ToString()), ("sigla", "XX"), ("nome_pt", "Variante"))
                .MontarZip();
            zip = new ZipDeTeste(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), versao);
            _zips[(versao, variante)] = zip;
        }

        return zip;
    }

    /// <summary>Valor do pacote como o registro da execução o guarda, com o resumo real do zip.</summary>
    public PacoteRecebido Recebido(string versao, char variante = 'a')
    {
        ZipDeTeste zip = Receber(versao, variante);
        return new PacoteRecebido(zip.Versao, zip.Sha256, zip.TamanhoEmBytes);
    }

    /// <summary>
    /// Envia o pacote ao registro e, em seguida, solta a trava das cargas que ficaram na fila.
    /// </summary>
    public async Task<Result<EnvioDoPacoteRegistrado>> EnviarAsync(GeoPostgisFixture fixture, ZipDeTeste pacote)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        await using GeoDbContext contexto = fixture.CreateDbContext();
        GeoImportacaoFila fila = new();
        RegistroDasImportacoes registro = CriarRegistro(
            contexto,
            fila,
            TravaDaCargaParaTeste.Criar(fixture.ConnectionString),
            TimeProvider.System,
            NullLogger<RegistroDasImportacoes>.Instance);

        Result<EnvioDoPacoteRegistrado> resultado = await registro.IniciarAsync(pacote.Abrir(), "teste", CancellationToken.None);
        foreach (CargaReservada enfileirada in fila.DrenarRestante())
        {
            await enfileirada.DisposeAsync();
        }

        return resultado;
    }

    public RegistroDasImportacoes CriarRegistro(
        GeoDbContext contexto,
        IGeoImportacaoFila fila,
        TravaConsultivaDaCargaDne trava,
        TimeProvider relogio,
        ILogger<RegistroDasImportacoes> logger) =>
        new(
            contexto,
            new EncerramentoDasCargasAbandonadas(contexto, Fonte, trava, relogio, NullLogger<EncerramentoDasCargasAbandonadas>.Instance),
            fila,
            trava,
            Arquivos,
            relogio,
            logger);

    public void Dispose()
    {
        if (Directory.Exists(_diretorio))
        {
            Directory.Delete(_diretorio, recursive: true);
        }
    }
}

/// <summary>Zip fictício do pacote, com o resumo SHA-256 e a versão que o registro deve encontrar.</summary>
internal sealed record ZipDeTeste(byte[] Bytes, string Sha256, string Versao)
{
    public long TamanhoEmBytes => Bytes.Length;

    public MemoryStream Abrir() => new(Bytes);
}
