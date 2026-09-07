using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Hub.Application.Adapters;
using Hub.Domain.Common;
using Hub.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hub.Infrastructure.TdApi;

public sealed class TdApiClient(
    HttpClient httpClient,
    IConfiguration configuration,
    IConditionalGetStore etagStore,
    ILogger<TdApiClient> logger) : ITdApiClient
{
    private const int PageSize = 500;
    private const int MaxPaginas = 100;
    private const string TitulosRequestUri = "v1/titulos";
    private const string DataFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<TitulosResponse>> GetTitulosAsync(CancellationToken cancellationToken)
    {
        if (!TryGetBaseUrl(out _))
        {
            return AdapterErrors.TdApiUrlNaoConfigurada;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, TitulosRequestUri);
        if (etagStore.TryGet(TitulosRequestUri, out var cachedEtag) && cachedEtag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", cachedEtag);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch titulos from TD API.");
            return AdapterErrors.TdApiHttpError;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new TitulosResponse(true, []);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("TD API returned {StatusCode} for {RequestUri}.", response.StatusCode, TitulosRequestUri);
                return AdapterErrors.TdApiHttpError;
            }

            List<TituloResponse?>? titulosBrutos;
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                titulosBrutos = await JsonSerializer.DeserializeAsync<List<TituloResponse?>>(stream, JsonOptions, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to read or deserialize titulos response from TD API.");
                return AdapterErrors.TdApiRespostaInvalida;
            }

            if (titulosBrutos is null)
            {
                return AdapterErrors.TdApiRespostaInvalida;
            }

            var titulos = new List<TituloResponse>(titulosBrutos.Count);
            var descartados = 0;
            foreach (var tituloBruto in titulosBrutos)
            {
                if (tituloBruto is null)
                {
                    descartados++;
                    continue;
                }

                titulos.Add(tituloBruto);
            }

            if (descartados > 0)
            {
                logger.LogWarning("Discarded {Descartados} null titulos from TD API response.", descartados);
            }

            var newEtag = response.Headers.ETag?.ToString();
            if (newEtag is not null)
            {
                etagStore.Set(TitulosRequestUri, newEtag);
            }

            return new TitulosResponse(false, titulos);
        }
    }

    public async Task<Result<AncoraPrecos>> ObterAncoraAsync(string codigo, CancellationToken cancellationToken)
    {
        if (!TryGetBaseUrl(out _))
        {
            logger.LogError("TdApi:BaseUrl is not configured or is not a valid absolute URL.");
            return AdapterErrors.TdApiUrlNaoConfigurada;
        }

        var requestUri = BuildAncoraRequestUri(codigo);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(requestUri, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch ancora de precos for {Codigo} from TD API.", codigo);
            return AdapterErrors.TdApiHttpError;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("TD API returned {StatusCode} for ancora of {Codigo}.", response.StatusCode, codigo);
                return AdapterErrors.TdApiHttpError;
            }

            var totalCount = 0;
            var hasTotalCount = response.Headers.TryGetValues("X-Total-Count", out var totalCountValues)
                && int.TryParse(totalCountValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out totalCount)
                && totalCount > 0;

            List<PrecoTaxaResponse?>? precosBrutos;
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                precosBrutos = await JsonSerializer.DeserializeAsync<List<PrecoTaxaResponse?>>(stream, JsonOptions, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to read or deserialize ancora response from TD API for {Codigo}.", codigo);
                return AdapterErrors.TdApiRespostaInvalida;
            }

            if (precosBrutos is null)
            {
                return AdapterErrors.TdApiRespostaInvalida;
            }

            var precos = new List<PrecoTaxaResponse>(precosBrutos.Count);
            var descartados = 0;
            foreach (var itemBruto in precosBrutos)
            {
                if (itemBruto is null)
                {
                    descartados++;
                    continue;
                }

                precos.Add(itemBruto);
            }

            if (descartados > 0)
            {
                logger.LogWarning("Discarded {Descartados} null precos in ancora response for {Codigo}.", descartados, codigo);
            }

            var total = hasTotalCount ? totalCount : precos.Count;

            if (precos.Count == 0)
            {
                return new AncoraPrecos(null, total);
            }

            if (!DateOnly.TryParseExact(
                precos[0].DataBase, DataFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var primeiraData))
            {
                logger.LogError(
                    "TD API returned unparseable dataBase {DataBase} in ancora for {Codigo}.", precos[0].DataBase, codigo);
                return AdapterErrors.TdApiRespostaInvalida;
            }

            return new AncoraPrecos(primeiraData, total);
        }
    }

    public async IAsyncEnumerable<Result<PrecoTaxaResponse>> GetPrecosAsync(
        string codigo,
        DateOnly dataInicio,
        DateOnly dataFim,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!TryGetBaseUrl(out _))
        {
            logger.LogError("TdApi:BaseUrl is not configured or is not a valid absolute URL.");
            yield return AdapterErrors.TdApiUrlNaoConfigurada;
            yield break;
        }

        var page = 1;
        var itemsFetched = 0;
        int? totalCount = null;
        var coletaCompleta = false;

        while (page <= MaxPaginas)
        {
            var requestUri = BuildPrecosRequestUri(codigo, dataInicio, dataFim, page);

            HttpResponseMessage? response = null;
            Error? falhaDeEnvio = null;
            try
            {
                response = await httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch precos for {Codigo} from TD API.", codigo);
                falhaDeEnvio = AdapterErrors.TdApiHttpError;
            }

            if (falhaDeEnvio is not null)
            {
                yield return falhaDeEnvio;
                yield break;
            }

            List<PrecoTaxaResponse?>? paginaBruta = null;
            Error? falhaDeLeitura = null;
            using (response)
            {
                if (!response!.IsSuccessStatusCode)
                {
                    logger.LogError(
                        "TD API returned {StatusCode} for precos of {Codigo}.", response.StatusCode, codigo);
                    falhaDeLeitura = AdapterErrors.TdApiHttpError;
                }
                else
                {
                    if (page == 1
                        && response.Headers.TryGetValues("X-Total-Count", out var totalCountValues)
                        && int.TryParse(
                            totalCountValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)
                        && total > 0)
                    {
                        totalCount = total;
                    }

                    try
                    {
                        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        paginaBruta = await JsonSerializer.DeserializeAsync<List<PrecoTaxaResponse?>>(stream, JsonOptions, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to read or deserialize precos response from TD API for {Codigo}.", codigo);
                        falhaDeLeitura = AdapterErrors.TdApiRespostaInvalida;
                    }
                }
            }

            if (falhaDeLeitura is not null)
            {
                yield return falhaDeLeitura;
                yield break;
            }

            if (paginaBruta is null)
            {
                yield return AdapterErrors.TdApiRespostaInvalida;
                yield break;
            }

            itemsFetched += paginaBruta.Count;

            var descartados = 0;
            var precos = new List<PrecoTaxaResponse>(paginaBruta.Count);
            foreach (var itemBruto in paginaBruta)
            {
                if (itemBruto is null)
                {
                    descartados++;
                    continue;
                }

                precos.Add(itemBruto);
            }

            if (descartados > 0)
            {
                logger.LogWarning("Discarded {Descartados} null precos for {Codigo}.", descartados, codigo);
            }

            if (totalCount is int totalAnunciadoNestaPagina && itemsFetched > totalAnunciadoNestaPagina)
            {
                logger.LogWarning(
                    "TD API announced X-Total-Count {TotalAnunciado} but the collection already gathered {Coletados} " +
                    "items for {Codigo}; discarding the inconsistent header and following pagination by page size.",
                    totalAnunciadoNestaPagina, itemsFetched, codigo);
                totalCount = null;
            }

            foreach (var preco in precos)
            {
                yield return preco;
            }

            if (paginaBruta.Count == 0 || paginaBruta.Count < PageSize)
            {
                coletaCompleta = true;
                break;
            }

            if (totalCount is int totalConhecido && itemsFetched == totalConhecido)
            {
                coletaCompleta = true;
                break;
            }

            page++;
        }

        if (!coletaCompleta)
        {
            logger.LogError(
                "Collection of precos for {Codigo} reached the {MaxPaginas}-page limit without completing; " +
                "discarding the partial result.",
                codigo, MaxPaginas);
            yield return AdapterErrors.TdApiColetaIncompleta;
            yield break;
        }

        if (totalCount is int totalAnunciado && itemsFetched < totalAnunciado)
        {
            logger.LogError(
                "TD API announced X-Total-Count {TotalAnunciado} but the collection gathered only {Coletados} items " +
                "for {Codigo}; discarding the truncated result.",
                totalAnunciado, itemsFetched, codigo);
            yield return AdapterErrors.TdApiColetaIncompleta;
        }
    }

    private bool TryGetBaseUrl(out Uri? baseUri)
    {
        var baseUrl = configuration["TdApi:BaseUrl"];
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out baseUri)
            && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps);
    }

    private static string BuildPrecosRequestUri(string codigo, DateOnly dataInicio, DateOnly dataFim, int page)
    {
        var codigoSegment = Uri.EscapeDataString(codigo);
        var dataInicioValue = dataInicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dataFimValue = dataFim.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return $"v1/titulos/{codigoSegment}/precos?dataInicio={dataInicioValue}&dataFim={dataFimValue}&page={page}&pageSize={PageSize}";
    }

    private static string BuildAncoraRequestUri(string codigo)
    {
        var codigoSegment = Uri.EscapeDataString(codigo);

        return $"v1/titulos/{codigoSegment}/precos?page=1&pageSize=1";
    }
}
