using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Hub.Application.Adapters;
using Hub.Application.Common.Interfaces;
using Hub.Application.Instrumentos;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Precos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hub.Infrastructure.TdApi;

public sealed class TdApiAdapter(
    ITdApiClient client,
    IInstrumentoWriteRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<TdApiAdapter> logger) : IPriceSourceAdapter
{
    private const string FonteTdApi = "td-api";
    private const int JanelaBackfillAnosPadrao = 2;
    private const string DataFormat = "yyyy-MM-dd";

    private static readonly DateOnly PisoProgramaPadrao = new(2002, 1, 7);

    public string Fonte => FonteTdApi;

    public async Task<Result<DiscoveryResultado>> DiscoverAsync(CancellationToken ct)
    {
        var titulosResult = await client.GetTitulosAsync(ct);
        if (titulosResult.IsFailure)
        {
            logger.LogError(
                "Failed to discover titulos from TD API: {Code} - {Description}",
                titulosResult.Error.Code, titulosResult.Error.Description);
            return titulosResult.Error;
        }

        var titulosResponse = titulosResult.Value;
        if (titulosResponse.NaoModificado)
        {
            logger.LogDebug("TD API titulos response not modified since last check; skipping discovery.");
            return new DiscoveryResultado(FonteTemNovidade: false, 0, 0, 0);
        }

        var fonteResult = Hub.Domain.Fontes.Fonte.Create(FonteTdApi);
        if (fonteResult.IsFailure)
        {
            logger.LogError("Failed to build td-api Fonte value object: {Description}", fonteResult.Error.Description);
            return fonteResult.Error;
        }

        var fonte = fonteResult.Value;

        var instrumentosResult = await repository.ListarPorClasseAsync(ClasseInstrumento.Td, ct);
        if (instrumentosResult.IsFailure)
        {
            logger.LogError("Failed to list existing td instruments: {Description}", instrumentosResult.Error.Description);
            return instrumentosResult.Error;
        }

        var fontesResult = await repository.ListarFontesAsync(fonte, ct);
        if (fontesResult.IsFailure)
        {
            logger.LogError("Failed to list existing td-api instrumento_fontes: {Description}", fontesResult.Error.Description);
            return fontesResult.Error;
        }

        var instrumentosPorId = instrumentosResult.Value.ToDictionary(i => i.Id.Value);
        var fontesPorCodigo = fontesResult.Value.ToDictionary(f => f.CodigoNaFonte.Value);

        var criados = 0;
        var atualizados = 0;
        var inalterados = 0;
        var agora = timeProvider.GetUtcNow();

        foreach (var titulo in titulosResponse.Titulos)
        {
            var codigoResult = CodigoNaFonte.Create(titulo.Codigo);
            if (codigoResult.IsFailure)
            {
                logger.LogWarning(
                    "Skipping titulo with invalid codigo {Codigo}: {Description}", titulo.Codigo, codigoResult.Error.Description);
                continue;
            }

            var codigo = codigoResult.Value;

            var idResult = InstrumentoId.Create($"td:{codigo.Value}");
            if (idResult.IsFailure)
            {
                logger.LogWarning(
                    "Skipping titulo with invalid codigo {Codigo}: {Description}", titulo.Codigo, idResult.Error.Description);
                continue;
            }

            var id = idResult.Value;

            if (!DateOnly.TryParseExact(
                titulo.DataVencimento, DataFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ativoAte))
            {
                logger.LogWarning(
                    "Skipping titulo {Codigo} with unparseable dataVencimento {DataVencimento}",
                    titulo.Codigo, titulo.DataVencimento);
                continue;
            }

            var nomeExibicao = $"{titulo.TipoTitulo} {titulo.DataVencimento}";

            var metadados = Metadados.Create(
                JsonSerializer.Serialize(new { indexador = titulo.Indexador, tipo = titulo.TipoTitulo })).Value;

            var instrumentoOk = true;

            if (instrumentosPorId.TryGetValue(id.Value, out var existente))
            {
                if (existente.DifereDoCatalogo(nomeExibicao, ativoAte, titulo.PagaJurosSemestrais, metadados))
                {
                    var atualizarResult = existente.AtualizarCatalogo(
                        nomeExibicao, ativoAte, titulo.PagaJurosSemestrais, metadados);

                    if (atualizarResult.IsFailure)
                    {
                        logger.LogWarning(
                            "Skipping catalog update for {Codigo}: {Description}",
                            titulo.Codigo, atualizarResult.Error.Description);
                        instrumentoOk = false;
                    }
                    else
                    {
                        logger.LogWarning("Catalog changed for TD instrument {Id}.", id.Value);
                        atualizados++;
                    }
                }
                else
                {
                    inalterados++;
                }
            }
            else
            {
                var instrumentoResult = Instrumento.Create(
                    id, nomeExibicao, ativoDesde: null, ativoAte, titulo.PagaJurosSemestrais, metadados, agora);

                if (instrumentoResult.IsFailure)
                {
                    logger.LogWarning(
                        "Skipping titulo {Codigo}: {Description}", titulo.Codigo, instrumentoResult.Error.Description);
                    instrumentoOk = false;
                }
                else
                {
                    var addResult = await repository.AdicionarAsync(instrumentoResult.Value, ct);
                    if (addResult.IsFailure)
                    {
                        logger.LogWarning(
                            "Failed to stage new instrument {Codigo}: {Description}",
                            titulo.Codigo, addResult.Error.Description);
                        instrumentoOk = false;
                    }
                    else
                    {
                        instrumentosPorId[id.Value] = instrumentoResult.Value;
                        criados++;
                    }
                }
            }

            if (!instrumentoOk)
            {
                continue;
            }

            if (fontesPorCodigo.ContainsKey(codigo.Value))
            {
                continue;
            }

            var instrumentoFonteResult = InstrumentoFonte.Create(id, fonte, codigo);
            if (instrumentoFonteResult.IsFailure)
            {
                logger.LogWarning(
                    "Skipping instrumento_fonte for {Codigo}: {Description}",
                    titulo.Codigo, instrumentoFonteResult.Error.Description);
                continue;
            }

            var addFonteResult = await repository.AdicionarFonteAsync(instrumentoFonteResult.Value, ct);
            if (addFonteResult.IsFailure)
            {
                logger.LogWarning(
                    "Failed to stage instrumento_fonte for {Codigo}: {Description}",
                    titulo.Codigo, addFonteResult.Error.Description);
                continue;
            }

            fontesPorCodigo[codigo.Value] = instrumentoFonteResult.Value;
        }

        var saveResult = await unitOfWork.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            logger.LogWarning(
                "Failed to persist TD API discovery changes: {Code} - {Description}; discovery will be retried on the next cycle.",
                saveResult.Error.Code, saveResult.Error.Description);
            return saveResult.Error;
        }

        logger.LogInformation(
            "TD API discovery completed: {Criados} created, {Atualizados} updated, {Inalterados} unchanged.",
            criados, atualizados, inalterados);

        return new DiscoveryResultado(FonteTemNovidade: true, criados, atualizados, inalterados);
    }

    public async IAsyncEnumerable<PrecoLido> FetchAsync(
        string codigoNaFonte, DateOnly dataInicio, [EnumeratorCancellation] CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (dataInicio > hoje)
        {
            yield break;
        }

        var fonteResult = Hub.Domain.Fontes.Fonte.Create(FonteTdApi);
        if (fonteResult.IsFailure)
        {
            logger.LogError(
                "Failed to build td-api Fonte value object while fetching prices for {Codigo}: {Description}",
                codigoNaFonte, fonteResult.Error.Description);
            yield break;
        }

        var fonte = fonteResult.Value;

        var instrumentoIdResult = InstrumentoId.Create($"td:{codigoNaFonte}");
        if (instrumentoIdResult.IsFailure)
        {
            logger.LogError(
                "Failed to build InstrumentoId while fetching prices for {Codigo}: {Description}",
                codigoNaFonte, instrumentoIdResult.Error.Description);
            yield break;
        }

        var instrumentoId = instrumentoIdResult.Value;
        var janelaAnos = ResolverJanelaBackfillAnos();
        var piso = ResolverPisoPrograma();
        var efetivoInicio = dataInicio;

        if (dataInicio <= piso)
        {
            var ancoraResult = await client.ObterAncoraAsync(codigoNaFonte, ct);

            if (ancoraResult.IsFailure)
            {
                logger.LogWarning(
                    "Failed to fetch ancora for {Codigo}: {Description}; falling back to dataInicio {DataInicio}.",
                    codigoNaFonte, ancoraResult.Error.Description, dataInicio);
            }
            else if (ancoraResult.Value.PrimeiraData is null)
            {
                logger.LogInformation(
                    "TD API has no prices at all for {Codigo}; skipping backfill.", codigoNaFonte);
                yield break;
            }
            else
            {
                var ancora = ancoraResult.Value.PrimeiraData.Value;

                if (ancora < piso || ancora > hoje)
                {
                    logger.LogWarning(
                        "Ancora {Ancora} for {Codigo} is outside of [{Piso}, {Hoje}]; falling back to dataInicio {DataInicio}.",
                        ancora, codigoNaFonte, piso, hoje, dataInicio);
                }
                else
                {
                    efetivoInicio = ancora;
                }
            }
        }

        var linha = 0;

        foreach (var (janelaInicio, janelaFim) in ConstruirJanelas(efetivoInicio, hoje, janelaAnos))
        {
            var truncado = false;

            await foreach (var precoResult in client.GetPrecosAsync(codigoNaFonte, janelaInicio, janelaFim, ct))
            {
                linha++;

                if (precoResult.IsFailure)
                {
                    yield return new PrecoLido(linha, precoResult.Error);
                    truncado = true;
                    break;
                }

                var preco = precoResult.Value;

                if (!DateOnly.TryParseExact(
                    preco.DataBase, DataFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataBase))
                {
                    logger.LogWarning(
                        "Skipping preco with unparseable dataBase {DataBase} for {Codigo}",
                        preco.DataBase, codigoNaFonte);
                    yield return new PrecoLido(linha, AdapterErrors.TdApiDataBaseInvalida);
                    continue;
                }

                var dataRefResult = DataRef.Create(dataBase);
                if (dataRefResult.IsFailure)
                {
                    logger.LogWarning(
                        "Skipping preco with invalid dataBase {DataBase} for {Codigo}: {Description}",
                        preco.DataBase, codigoNaFonte, dataRefResult.Error.Description);
                    yield return new PrecoLido(linha, dataRefResult.Error);
                    continue;
                }

                var dataRef = dataRefResult.Value;
                var observadoEm = timeProvider.GetUtcNow();

                foreach (var priceObserved in CriarPriceObserved(instrumentoId, dataRef, fonte, observadoEm, preco))
                {
                    yield return new PrecoLido(linha, priceObserved);
                }
            }

            if (truncado)
            {
                yield break;
            }
        }
    }

    private static IEnumerable<PriceObserved> CriarPriceObserved(
        InstrumentoId instrumentoId,
        DataRef dataRef,
        Hub.Domain.Fontes.Fonte fonte,
        DateTimeOffset observadoEm,
        PrecoTaxaResponse preco)
    {
        (string Nome, decimal? Valor)[] campos =
        [
            (Campos.PuVenda, preco.PuVenda),
            (Campos.PuCompra, preco.PuCompra),
            (Campos.TaxaVenda, preco.TaxaVenda),
            (Campos.TaxaCompra, preco.TaxaCompra),
            (Campos.PuBase, preco.PuBase),
        ];

        foreach (var (nome, valor) in campos)
        {
            if (valor is null)
            {
                continue;
            }

            var campoResult = Campo.Create(nome);
            if (campoResult.IsFailure)
            {
                continue;
            }

            yield return new PriceObserved(instrumentoId, dataRef, campoResult.Value, fonte, valor.Value, observadoEm);
        }
    }

    private int ResolverJanelaBackfillAnos()
    {
        var configurado = configuration["TdApi:JanelaBackfillAnos"];

        if (string.IsNullOrWhiteSpace(configurado))
        {
            return JanelaBackfillAnosPadrao;
        }

        if (!int.TryParse(configurado, NumberStyles.Integer, CultureInfo.InvariantCulture, out var janela)
            || janela < 1)
        {
            logger.LogWarning(
                "TdApi:JanelaBackfillAnos configured with invalid value {Configurado}; falling back to default {Default}.",
                configurado, JanelaBackfillAnosPadrao);
            return JanelaBackfillAnosPadrao;
        }

        return janela;
    }

    private DateOnly ResolverPisoPrograma()
    {
        var configurado = configuration["TdApi:PisoPrograma"];

        if (string.IsNullOrWhiteSpace(configurado))
        {
            return PisoProgramaPadrao;
        }

        if (!DateOnly.TryParseExact(
            configurado, DataFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var piso))
        {
            logger.LogWarning(
                "TdApi:PisoPrograma configured with invalid value {Configurado}; falling back to default {Default}.",
                configurado, PisoProgramaPadrao);
            return PisoProgramaPadrao;
        }

        return piso;
    }

    private static IEnumerable<(DateOnly Inicio, DateOnly Fim)> ConstruirJanelas(DateOnly dataInicio, DateOnly hoje, int janelaAnos)
    {
        var inicio = dataInicio;

        while (inicio <= hoje)
        {
            var fimCandidato = inicio.AddYears(janelaAnos).AddDays(-1);
            var fim = fimCandidato > hoje ? hoje : fimCandidato;

            yield return (inicio, fim);

            inicio = fim.AddDays(1);
        }
    }
}
