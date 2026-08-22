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
    private const string TitulosRequestUri = "v1/titulos";

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

            List<TituloResponse>? titulos;
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                titulos = await JsonSerializer.DeserializeAsync<List<TituloResponse>>(stream, JsonOptions, cancellationToken);
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

            if (titulos is null)
            {
                return AdapterErrors.TdApiRespostaInvalida;
            }

            var newEtag = response.Headers.ETag?.ToString();
            if (newEtag is not null)
            {
                etagStore.Set(TitulosRequestUri, newEtag);
            }

            return new TitulosResponse(false, titulos);
        }
    }

    public async IAsyncEnumerable<PrecoTaxaResponse> GetPrecosAsync(
        string codigo,
        DateOnly dataInicio,
        DateOnly dataFim,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!TryGetBaseUrl(out _))
        {
            logger.LogError("TdApi:BaseUrl is not configured or is not a valid absolute URL.");
            yield break;
        }

        var page = 1;
        var itemsFetched = 0;
        var hasTotalCount = false;
        var totalCount = 0;

        do
        {
            var requestUri = BuildPrecosRequestUri(codigo, dataInicio, dataFim, page);

            HttpResponseMessage response;
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
                yield break;
            }

            List<PrecoTaxaResponse>? precos;
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogError(
                        "TD API returned {StatusCode} for precos of {Codigo}.", response.StatusCode, codigo);
                    yield break;
                }

                if (page == 1)
                {
                    hasTotalCount = response.Headers.TryGetValues("X-Total-Count", out var totalCountValues)
                        && int.TryParse(totalCountValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out totalCount);
                }

                try
                {
                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    precos = await JsonSerializer.DeserializeAsync<List<PrecoTaxaResponse>>(stream, JsonOptions, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to read or deserialize precos response from TD API for {Codigo}.", codigo);
                    yield break;
                }
            }

            if (precos is null || precos.Count == 0)
            {
                yield break;
            }

            foreach (var preco in precos)
            {
                yield return preco;
            }

            itemsFetched += precos.Count;
            page++;
        }
        while (hasTotalCount && itemsFetched < totalCount);
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
}
