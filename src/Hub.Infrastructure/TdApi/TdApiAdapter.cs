using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Hub.Application.Adapters;
using Hub.Application.Common.Interfaces;
using Hub.Application.Instrumentos;
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

    public string Fonte => FonteTdApi;

    public async Task DiscoverAsync(CancellationToken ct)
    {
        var titulosResult = await client.GetTitulosAsync(ct);
        if (titulosResult.IsFailure)
        {
            logger.LogError(
                "Failed to discover titulos from TD API: {Code} - {Description}",
                titulosResult.Error.Code, titulosResult.Error.Description);
            return;
        }

        var titulosResponse = titulosResult.Value;
        if (titulosResponse.NaoModificado)
        {
            logger.LogDebug("TD API titulos response not modified since last check; skipping discovery.");
            return;
        }

        var fonteResult = Hub.Domain.Fontes.Fonte.Create(FonteTdApi);
        if (fonteResult.IsFailure)
        {
            logger.LogError("Failed to build td-api Fonte value object: {Description}", fonteResult.Error.Description);
            return;
        }

        var fonte = fonteResult.Value;

        var instrumentosResult = await repository.ListarPorClasseAsync(ClasseInstrumento.Td, ct);
        if (instrumentosResult.IsFailure)
        {
            logger.LogError("Failed to list existing td instruments: {Description}", instrumentosResult.Error.Description);
            return;
        }

        var fontesResult = await repository.ListarFontesAsync(fonte, ct);
        if (fontesResult.IsFailure)
        {
            logger.LogError("Failed to list existing td-api instrumento_fontes: {Description}", fontesResult.Error.Description);
            return;
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
            var metadados = JsonSerializer.Serialize(new { indexador = titulo.Indexador, tipo = titulo.TipoTitulo });

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

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "TD API discovery completed: {Criados} created, {Atualizados} updated, {Inalterados} unchanged.",
            criados, atualizados, inalterados);
    }

    public async IAsyncEnumerable<PriceObserved> FetchAsync(
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
            yield break;
        }

        var fonte = fonteResult.Value;

        var instrumentoIdResult = InstrumentoId.Create($"td:{codigoNaFonte}");
        if (instrumentoIdResult.IsFailure)
        {
            yield break;
        }

        var instrumentoId = instrumentoIdResult.Value;
        var janelaAnos = ResolverJanelaBackfillAnos();

        foreach (var (janelaInicio, janelaFim) in ConstruirJanelas(dataInicio, hoje, janelaAnos))
        {
            await foreach (var preco in client.GetPrecosAsync(codigoNaFonte, janelaInicio, janelaFim, ct))
            {
                if (!DateOnly.TryParseExact(
                    preco.DataBase, DataFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataBase))
                {
                    logger.LogWarning(
                        "Skipping preco with unparseable dataBase {DataBase} for {Codigo}",
                        preco.DataBase, codigoNaFonte);
                    continue;
                }

                var dataRefResult = DataRef.Create(dataBase);
                if (dataRefResult.IsFailure)
                {
                    logger.LogWarning(
                        "Skipping preco with invalid dataBase {DataBase} for {Codigo}: {Description}",
                        preco.DataBase, codigoNaFonte, dataRefResult.Error.Description);
                    continue;
                }

                var dataRef = dataRefResult.Value;
                var observadoEm = timeProvider.GetUtcNow();

                foreach (var priceObserved in CriarPriceObserved(instrumentoId, dataRef, fonte, observadoEm, preco))
                {
                    yield return priceObserved;
                }
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
        var configurado = configuration.GetValue<int?>("TdApi:JanelaBackfillAnos");

        if (configurado is null)
        {
            return JanelaBackfillAnosPadrao;
        }

        if (configurado.Value < 1)
        {
            logger.LogWarning(
                "TdApi:JanelaBackfillAnos configured with invalid value {Configurado}; falling back to default {Default}.",
                configurado.Value, JanelaBackfillAnosPadrao);
            return JanelaBackfillAnosPadrao;
        }

        return configurado.Value;
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
