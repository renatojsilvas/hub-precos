using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Text.Json;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Precos;
using Hub.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class PricesAsOfEndpointsTests
{
    private readonly ApiTestFactory _factory;
    private readonly HttpClient _client;

    public PricesAsOfEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NovoId(string classe = "td") => $"{classe}:asof-http-{Guid.NewGuid():N}";

    private async Task<InstrumentoId> CriarInstrumentoAsync(string id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var instrumentoId = InstrumentoId.Create(id).Value;
        var instrumento = Instrumento.Create(
            instrumentoId,
            nomeExibicao: id,
            ativoDesde: null,
            ativoAte: null,
            pagaCupom: false,
            metadados: Metadados.Create("{}").Value,
            criadoEm: DateTimeOffset.UtcNow).Value;

        db.Instrumentos.Add(instrumento);
        await db.SaveChangesAsync();

        return instrumentoId;
    }

    private async Task RegistrarPrecoAsync(
        InstrumentoId instrumentoId, DateOnly dataRef, string campo, string fonte,
        int revisao, decimal valor, DateTimeOffset? observadoEm = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var preco = Preco.Create(
            instrumentoId,
            DataRef.Create(dataRef).Value,
            Campo.Create(campo).Value,
            Fonte.Create(fonte).Value,
            valor,
            revisao,
            observadoEm ?? DateTimeOffset.UtcNow).Value;

        db.Precos.Add(preco);
        await db.SaveChangesAsync();
    }

    private static HttpRequestMessage ConditionalGet(string uri, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        return request;
    }

    [Fact]
    public async Task Get_FirstCallWithoutIfNoneMatch_ShouldReturn200WithEtagAndCacheControl()
    {
        var response = await _client.GetAsync("/v1/prices/asof?date=2026-08-14", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromSeconds(300), response.Headers.CacheControl.MaxAge);
    }

    [Fact]
    public async Task Get_SecondCallWithSameIfNoneMatch_ShouldReturn304WithoutBody()
    {
        var first = await _client.GetAsync("/v1/prices/asof?date=2026-08-14", CancellationToken.None);
        var etag = first.Headers.ETag!.Tag;

        using var conditional = ConditionalGet("/v1/prices/asof?date=2026-08-14", etag);
        var second = await _client.SendAsync(conditional, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.Empty(body);
    }

    [Fact]
    public async Task Get_AfterNewPriceInserted_ShouldChangeEtag()
    {
        var instrumentoId = await CriarInstrumentoAsync(NovoId());

        var first = await _client.GetAsync("/v1/prices/asof?date=2026-08-14", CancellationToken.None);
        var etag = first.Headers.ETag!.Tag;

        await RegistrarPrecoAsync(instrumentoId, new DateOnly(2026, 8, 14), "pu_venda", "td-api", 0, 100m);

        using var stale = ConditionalGet("/v1/prices/asof?date=2026-08-14", etag);
        var afterInsert = await _client.SendAsync(stale, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, afterInsert.StatusCode);
        Assert.NotEqual(etag, afterInsert.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Head_ShouldReturn200WithoutBodyAndSameEtagAsGet()
    {
        var get = await _client.GetAsync("/v1/prices/asof?date=2026-08-14", CancellationToken.None);

        using var headRequest = new HttpRequestMessage(HttpMethod.Head, "/v1/prices/asof?date=2026-08-14");
        var head = await _client.SendAsync(headRequest, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        var body = await head.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.Empty(body);

        Assert.NotNull(head.Headers.ETag);
        Assert.Equal(get.Headers.ETag!.Tag, head.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Options_ShouldReturn204WithAllowHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/prices/asof");
        var response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var allow = string.Join(",", response.Content.Headers.Allow);
        Assert.Contains("GET", allow);
        Assert.Contains("HEAD", allow);
        Assert.Contains("OPTIONS", allow);
    }

    [Fact]
    public async Task Delete_ShouldReturn405WithAllowHeaderContainingGetHeadOptions()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/prices/asof");
        var response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var allow = string.Join(",", response.Content.Headers.Allow);
        Assert.Contains("GET", allow);
        Assert.Contains("HEAD", allow);
        Assert.Contains("OPTIONS", allow);
    }

    [Fact]
    public async Task Get_WithInvalidDate_ShouldReturn400ProblemJsonWithoutEtag_AndRepeatingWithIfNoneMatchStaysAt400()
    {
        var response = await _client.GetAsync("/v1/prices/asof?date=not-a-date", CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.ETag);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("Preco.DataInvalida", document.RootElement.GetProperty("code").GetString());
        Assert.True(document.RootElement.TryGetProperty("correlationId", out var correlationId));
        Assert.False(string.IsNullOrEmpty(correlationId.GetString()));
        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrEmpty(traceId.GetString()));

        using var conditional = ConditionalGet("/v1/prices/asof?date=not-a-date", "\"qualquer-etag\"");
        var second = await _client.SendAsync(conditional, CancellationToken.None);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Get_WithoutDate_ShouldReturn400()
    {
        var response = await _client.GetAsync("/v1/prices/asof", CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_ForwardFill_ShouldReturnDataRefOfLastPriceBeforeRequestedDate()
    {
        var id = NovoId();
        var instrumentoId = await CriarInstrumentoAsync(id);
        await RegistrarPrecoAsync(instrumentoId, new DateOnly(2026, 8, 11), "pu_venda", "td-api", 0, 3496.412345m);

        var response = await _client.GetAsync($"/v1/prices/asof?date=2026-08-14&instruments={id}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal("2026-08-11", item.GetProperty("dataRef").GetString());
        Assert.Equal("2026-08-14", document.RootElement.GetProperty("date").GetString());
    }

    [Fact]
    public async Task Get_InstrumentoSemPrecoAteAData_DeveAparecerComMotivoSemPrecoAteAData()
    {
        var id = NovoId();
        var instrumentoId = await CriarInstrumentoAsync(id);
        await RegistrarPrecoAsync(instrumentoId, new DateOnly(2026, 8, 20), "pu_venda", "td-api", 0, 100m);

        var response = await _client.GetAsync($"/v1/prices/asof?date=2026-08-14&instruments={id}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.GetProperty("items")[0];

        Assert.Equal("sem_preco_ate_a_data", item.GetProperty("motivo").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("campos").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("dataRef").ValueKind);
    }

    [Fact]
    public async Task Get_IdDesconhecidoNoCatalogo_DeveAparecerComMotivoInstrumentoDesconhecido()
    {
        var idInexistente = NovoId();

        var response = await _client.GetAsync(
            $"/v1/prices/asof?date=2026-08-14&instruments={idInexistente}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.GetProperty("items")[0];

        Assert.Equal("instrumento_desconhecido", item.GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task Get_ComRevisao0E1_DevolveOValorDaRevisao1()
    {
        var id = NovoId();
        var instrumentoId = await CriarInstrumentoAsync(id);
        var dataRef = new DateOnly(2026, 8, 14);

        await RegistrarPrecoAsync(instrumentoId, dataRef, "pu_venda", "td-api", 0, 100m);
        await RegistrarPrecoAsync(instrumentoId, dataRef, "pu_venda", "td-api", 1, 200m);

        var response = await _client.GetAsync($"/v1/prices/asof?date=2026-08-14&instruments={id}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var campo = document.RootElement.GetProperty("items")[0].GetProperty("campos").GetProperty("pu_venda");

        Assert.Equal(1, campo.GetProperty("revisao").GetInt32());
        Assert.Equal("200.000000", campo.GetProperty("valor").GetString());
    }

    [Fact]
    public async Task Get_ValorDeveSerSerializadoComoStringEntreAspas_NuncaComoNumeroJson()
    {
        var id = NovoId();
        var instrumentoId = await CriarInstrumentoAsync(id);
        await RegistrarPrecoAsync(instrumentoId, new DateOnly(2026, 8, 14), "pu_venda", "td-api", 0, 3496.412345m);

        var response = await _client.GetAsync($"/v1/prices/asof?date=2026-08-14&instruments={id}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Contains("\"valor\":\"3496.412345\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"valor\":3496.412345", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_ComInstrumentoTd_CampoPosicaoDeveSerPuVenda()
    {
        var id = NovoId();
        var instrumentoId = await CriarInstrumentoAsync(id);
        await RegistrarPrecoAsync(instrumentoId, new DateOnly(2026, 8, 14), "pu_venda", "td-api", 0, 100m);

        var response = await _client.GetAsync($"/v1/prices/asof?date=2026-08-14&instruments={id}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.GetProperty("items")[0];

        Assert.Equal("pu_venda", item.GetProperty("campoPosicao").GetString());
    }

    [Fact]
    public async Task Get_XTotalCountHeader_EhSempreEnviado()
    {
        var response = await _client.GetAsync("/v1/prices/asof?date=2026-08-14", CancellationToken.None);

        Assert.True(response.Headers.TryGetValues("X-Total-Count", out _));
    }

    [Fact]
    public async Task Get_SemPageParam_NaoDeveEnviarLinkHeader()
    {
        var response = await _client.GetAsync("/v1/prices/asof?date=2026-08-14", CancellationToken.None);

        Assert.False(response.Headers.Contains("Link"));
    }

    [Fact]
    public async Task Get_ComInstrumentsEPaginacao_LinkHeaderTemRelsCorretosNaPrimeiraNoMeioENaUltimaPagina()
    {
        var ids = Enumerable.Range(0, 12).Select(_ => NovoId()).ToList();
        foreach (var id in ids)
        {
            await CriarInstrumentoAsync(id);
        }

        var instrumentsParam = string.Join(',', ids);

        var first = await _client.GetAsync(
            $"/v1/prices/asof?date=2026-08-14&instruments={instrumentsParam}&page=1&pageSize=5", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.TryGetValues("X-Total-Count", out var totalFirst));
        Assert.Equal("12", totalFirst!.Single());
        Assert.True(first.Headers.TryGetValues("Link", out var linkFirstValues));
        var linkFirst = linkFirstValues!.Single();
        Assert.Contains("rel=\"first\"", linkFirst, StringComparison.Ordinal);
        Assert.Contains("rel=\"next\"", linkFirst, StringComparison.Ordinal);
        Assert.Contains("rel=\"last\"", linkFirst, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"prev\"", linkFirst, StringComparison.Ordinal);

        var middle = await _client.GetAsync(
            $"/v1/prices/asof?date=2026-08-14&instruments={instrumentsParam}&page=2&pageSize=5", CancellationToken.None);
        Assert.True(middle.Headers.TryGetValues("Link", out var linkMiddleValues));
        var linkMiddle = linkMiddleValues!.Single();
        Assert.Contains("rel=\"first\"", linkMiddle, StringComparison.Ordinal);
        Assert.Contains("rel=\"prev\"", linkMiddle, StringComparison.Ordinal);
        Assert.Contains("rel=\"next\"", linkMiddle, StringComparison.Ordinal);
        Assert.Contains("rel=\"last\"", linkMiddle, StringComparison.Ordinal);

        var last = await _client.GetAsync(
            $"/v1/prices/asof?date=2026-08-14&instruments={instrumentsParam}&page=3&pageSize=5", CancellationToken.None);
        var lastBody = await last.Content.ReadAsStringAsync(CancellationToken.None);
        using var lastDocument = JsonDocument.Parse(lastBody);
        Assert.Equal(2, lastDocument.RootElement.GetProperty("items").GetArrayLength());
        Assert.True(last.Headers.TryGetValues("Link", out var linkLastValues));
        var linkLast = linkLastValues!.Single();
        Assert.Contains("rel=\"first\"", linkLast, StringComparison.Ordinal);
        Assert.Contains("rel=\"prev\"", linkLast, StringComparison.Ordinal);
        Assert.Contains("rel=\"last\"", linkLast, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"next\"", linkLast, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_SemInstruments_UsaCatalogoENuncaExcedeCemItensSemPageInformado()
    {
        var response = await _client.GetAsync("/v1/prices/asof?date=2026-08-14", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);

        Assert.True(document.RootElement.GetProperty("items").GetArrayLength() <= 100);
    }

    [Fact]
    public async Task Get_ComInstrumentoIdRepetidoNaListaEComPreco_NaoRetorna500_DedupPreservaOrdem_XTotalCountContaDistintos()
    {
        var id = NovoId();
        var instrumentoId = await CriarInstrumentoAsync(id);
        await RegistrarPrecoAsync(instrumentoId, new DateOnly(2026, 8, 14), "pu_venda", "td-api", 0, 100m);
        await RegistrarPrecoAsync(instrumentoId, new DateOnly(2026, 8, 14), "taxa_venda", "td-api", 0, 6.18m);

        var response = await _client.GetAsync(
            $"/v1/prices/asof?date=2026-08-14&instruments={id},{id}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Total-Count", out var total));
        Assert.Equal("1", total!.Single());

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(1, document.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Get_ComInstrumentoIdRepetidoComDiferencaDeCaixa_DedupIgnorandoCaixa()
    {
        var id = NovoId();
        await CriarInstrumentoAsync(id);
        var idMaiusculo = id.ToUpperInvariant();

        var response = await _client.GetAsync(
            $"/v1/prices/asof?date=2026-08-14&instruments={id},{idMaiusculo}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Total-Count", out var total));
        Assert.Equal("1", total!.Single());
    }

    [Fact]
    public async Task Get_ComMaisDe100InstrumentsSemPage_CortaEm100EXTotalCountRefleteOTotalPedido()
    {
        var ids = Enumerable.Range(0, 120).Select(_ => NovoId()).ToList();
        var instrumentsParam = string.Join(',', ids);

        var response = await _client.GetAsync(
            $"/v1/prices/asof?date=2026-08-14&instruments={instrumentsParam}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Total-Count", out var total));
        Assert.Equal("120", total!.Single());

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var itemsLength = document.RootElement.GetProperty("items").GetArrayLength();

        Assert.Equal(100, itemsLength);
        Assert.NotEqual(itemsLength, int.Parse(total!.Single(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Get_SemInstrumentsEComPageIntMaxValue_Retorna200ComItemsVazioELinkCoerente()
    {
        var response = await _client.GetAsync(
            $"/v1/prices/asof?date=2026-08-14&page={int.MaxValue}&pageSize=500", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(0, document.RootElement.GetProperty("items").GetArrayLength());

        Assert.True(response.Headers.TryGetValues("X-Total-Count", out _));

        Assert.True(response.Headers.TryGetValues("Link", out var linkValues));
        var link = linkValues!.Single();
        Assert.Contains("rel=\"first\"", link, StringComparison.Ordinal);
        Assert.Contains("rel=\"last\"", link, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"next\"", link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_ComInstrumentsEPageIntMaxValue_Retorna200ComItemsVazioETotalCountELinkCoerentes()
    {
        var ids = Enumerable.Range(0, 3).Select(_ => NovoId()).ToList();
        foreach (var id in ids)
        {
            await CriarInstrumentoAsync(id);
        }

        var instrumentsParam = string.Join(',', ids);

        var response = await _client.GetAsync(
            $"/v1/prices/asof?date=2026-08-14&instruments={instrumentsParam}&page={int.MaxValue}&pageSize=500",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(0, document.RootElement.GetProperty("items").GetArrayLength());

        Assert.True(response.Headers.TryGetValues("X-Total-Count", out var total));
        Assert.Equal("3", total!.Single());

        Assert.True(response.Headers.TryGetValues("Link", out var linkValues));
        var link = linkValues!.Single();
        Assert.Contains("rel=\"first\"", link, StringComparison.Ordinal);
        Assert.Contains("rel=\"last\"", link, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"next\"", link, StringComparison.Ordinal);
    }
}
