using System.Net;
using System.Net.Mime;
using System.Text.Json;
using Hub.Domain.Instrumentos;
using Hub.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class InstrumentsEndpointsTests
{
    private readonly ApiTestFactory _factory;
    private readonly HttpClient _client;

    public InstrumentsEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NovoId(string classe = "td") => $"{classe}:instr-http-{Guid.NewGuid():N}";

    private async Task CriarInstrumentoAsync(
        string id,
        string? nomeExibicao = null,
        DateOnly? ativoAte = null,
        string metadados = "{}")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var instrumentoId = InstrumentoId.Create(id).Value;
        var instrumento = Instrumento.Create(
            instrumentoId,
            nomeExibicao: nomeExibicao ?? id,
            ativoDesde: null,
            ativoAte: ativoAte,
            pagaCupom: false,
            metadados: Metadados.Create(metadados).Value,
            criadoEm: DateTimeOffset.UtcNow).Value;

        db.Instrumentos.Add(instrumento);
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
        var response = await _client.GetAsync("/v1/instruments", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromSeconds(300), response.Headers.CacheControl.MaxAge);
    }

    [Fact]
    public async Task Get_SecondCallWithSameIfNoneMatch_ShouldReturn304WithoutBody()
    {
        var first = await _client.GetAsync("/v1/instruments", CancellationToken.None);
        var etag = first.Headers.ETag!.Tag;

        using var conditional = ConditionalGet("/v1/instruments", etag);
        var second = await _client.SendAsync(conditional, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.Empty(body);
    }

    [Fact]
    public async Task Head_ShouldReturn200WithoutBodyAndSameEtagAsGet()
    {
        var get = await _client.GetAsync("/v1/instruments", CancellationToken.None);

        using var headRequest = new HttpRequestMessage(HttpMethod.Head, "/v1/instruments");
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
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/instruments");
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
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/instruments");
        var response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var allow = string.Join(",", response.Content.Headers.Allow);
        Assert.Contains("GET", allow);
        Assert.Contains("HEAD", allow);
        Assert.Contains("OPTIONS", allow);
    }

    [Fact]
    public async Task Get_AfterNewInstrumentInserted_ShouldChangeEtag()
    {
        var first = await _client.GetAsync("/v1/instruments", CancellationToken.None);
        var etag = first.Headers.ETag!.Tag;

        await CriarInstrumentoAsync(NovoId());

        using var stale = ConditionalGet("/v1/instruments", etag);
        var afterInsert = await _client.SendAsync(stale, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, afterInsert.StatusCode);
        Assert.NotEqual(etag, afterInsert.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Get_ComFiltrosDeClasseDiferentes_GeraETagsDiferentes()
    {
        var td = await _client.GetAsync("/v1/instruments?classe=td", CancellationToken.None);
        var acao = await _client.GetAsync("/v1/instruments?classe=acao", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, td.StatusCode);
        Assert.Equal(HttpStatusCode.OK, acao.StatusCode);
        Assert.NotEqual(td.Headers.ETag!.Tag, acao.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Get_ComClasseInvalida_ShouldReturn400ProblemJsonWithoutEtag_AndRepeatingWithIfNoneMatchStaysAt400()
    {
        var response = await _client.GetAsync("/v1/instruments?classe=xpto", CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.ETag);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("ClasseInstrumento.Invalida", document.RootElement.GetProperty("code").GetString());
        Assert.True(document.RootElement.TryGetProperty("correlationId", out var correlationId));
        Assert.False(string.IsNullOrEmpty(correlationId.GetString()));
        Assert.True(document.RootElement.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrEmpty(traceId.GetString()));

        using var conditional = ConditionalGet("/v1/instruments?classe=xpto", "\"qualquer-etag\"");
        var second = await _client.SendAsync(conditional, CancellationToken.None);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Get_ComFiltroDeClasse_DevolveApenasInstrumentosDaquelaClasse()
    {
        var sufixo = Guid.NewGuid().ToString("N");
        var idTd = $"td:instr-filtro-classe-{sufixo}";
        var idAcao = $"acao:instr-filtro-classe-{sufixo}";
        await CriarInstrumentoAsync(idTd);
        await CriarInstrumentoAsync(idAcao);

        var respostaTd = await _client.GetAsync($"/v1/instruments?classe=td&query={sufixo}", CancellationToken.None);
        var corpoTd = await respostaTd.Content.ReadAsStringAsync(CancellationToken.None);
        using var documentoTd = JsonDocument.Parse(corpoTd);
        var idsTd = documentoTd.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();

        Assert.Contains(idTd, idsTd);
        Assert.DoesNotContain(idAcao, idsTd);

        var respostaAcao = await _client.GetAsync($"/v1/instruments?classe=acao&query={sufixo}", CancellationToken.None);
        var corpoAcao = await respostaAcao.Content.ReadAsStringAsync(CancellationToken.None);
        using var documentoAcao = JsonDocument.Parse(corpoAcao);
        var idsAcao = documentoAcao.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();

        Assert.Contains(idAcao, idsAcao);
        Assert.DoesNotContain(idTd, idsAcao);
    }

    [Fact]
    public async Task Get_ComQueryCasandoPorId_DevolveOInstrumento()
    {
        var sufixo = Guid.NewGuid().ToString("N");
        var id = $"td:instr-query-por-id-{sufixo}";
        await CriarInstrumentoAsync(id, nomeExibicao: "Nome sem relação com a busca");

        var response = await _client.GetAsync($"/v1/instruments?query={sufixo}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var ids = document.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();

        Assert.Contains(id, ids);
    }

    [Fact]
    public async Task Get_ComQueryCasandoPorNomeExibicao_DevolveOInstrumento()
    {
        var sufixo = Guid.NewGuid().ToString("N");
        var id = $"td:instr-query-por-nome-{sufixo}";
        var nomeExibicao = $"Título com termo NOME-BUSCA-{sufixo} no meio";
        await CriarInstrumentoAsync(id, nomeExibicao: nomeExibicao);

        var response = await _client.GetAsync($"/v1/instruments?query=nome-busca-{sufixo}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var ids = document.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();

        Assert.Contains(id, ids);
    }

    [Fact]
    public async Task Get_ComQuery_EhCaseInsensitive()
    {
        var sufixo = Guid.NewGuid().ToString("N");
        var id = $"td:instr-query-case-{sufixo}";
        await CriarInstrumentoAsync(id, nomeExibicao: $"Nome irrelevante {sufixo}");

        var response = await _client.GetAsync(
            $"/v1/instruments?query={$"INSTR-QUERY-CASE-{sufixo}".ToUpperInvariant()}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var ids = document.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();

        Assert.Contains(id, ids);
    }

    [Fact]
    public async Task Get_Vencido_TrueParaAtivoAteNoPassado()
    {
        var id = NovoId();
        await CriarInstrumentoAsync(id, ativoAte: new DateOnly(2020, 1, 1));

        var response = await _client.GetAsync($"/v1/instruments?query={id}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);

        Assert.True(item.GetProperty("vencido").GetBoolean());
        Assert.Equal("2020-01-01", item.GetProperty("ativoAte").GetString());
    }

    [Fact]
    public async Task Get_Vencido_FalseParaAtivoAteNoFuturo()
    {
        var id = NovoId();
        await CriarInstrumentoAsync(id, ativoAte: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)));

        var response = await _client.GetAsync($"/v1/instruments?query={id}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);

        Assert.False(item.GetProperty("vencido").GetBoolean());
    }

    [Fact]
    public async Task Get_Vencido_FalseParaAtivoAteNulo()
    {
        var id = NovoId();
        await CriarInstrumentoAsync(id, ativoAte: null);

        var response = await _client.GetAsync($"/v1/instruments?query={id}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);

        Assert.False(item.GetProperty("vencido").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("ativoAte").ValueKind);
    }

    [Fact]
    public async Task Get_ComInstrumentoTd_CampoPosicaoDeveSerPuVenda()
    {
        var id = NovoId("td");
        await CriarInstrumentoAsync(id);

        var response = await _client.GetAsync($"/v1/instruments?query={id}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);

        Assert.Equal("pu_venda", item.GetProperty("campoPosicao").GetString());
    }

    [Fact]
    public async Task Get_ComInstrumentoDeClasseSemRegraDeCampoPosicao_CampoPosicaoDeveSerNulo()
    {
        var id = NovoId("acao");
        await CriarInstrumentoAsync(id);

        var response = await _client.GetAsync($"/v1/instruments?query={id}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);

        Assert.Equal(JsonValueKind.Null, item.GetProperty("campoPosicao").ValueKind);
    }

    [Fact]
    public async Task Get_Metadados_SaiComoObjetoJsonNuncaComoStringEscapada()
    {
        var id = NovoId();
        await CriarInstrumentoAsync(id, metadados: "{\"indexador\":\"Selic\",\"tipo\":\"Tesouro Selic\"}");

        var response = await _client.GetAsync($"/v1/instruments?query={id}", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);

        Assert.Equal(JsonValueKind.Object, item.GetProperty("metadados").ValueKind);
        Assert.Equal("Selic", item.GetProperty("metadados").GetProperty("indexador").GetString());

        Assert.Contains("\"metadados\":{", body, StringComparison.Ordinal);
        Assert.Contains("\"indexador\":\"Selic\"", body, StringComparison.Ordinal);
        Assert.Contains("\"tipo\":\"Tesouro Selic\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"metadados\":\"{", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\"indexador\\\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_XTotalCountHeader_RefleteOTotalAposFiltros()
    {
        var sufixo = Guid.NewGuid().ToString("N");
        await CriarInstrumentoAsync($"td:instr-total-{sufixo}-a");
        await CriarInstrumentoAsync($"td:instr-total-{sufixo}-b");
        await CriarInstrumentoAsync($"acao:instr-total-{sufixo}-c");

        var response = await _client.GetAsync($"/v1/instruments?query={sufixo}&classe=td", CancellationToken.None);

        Assert.True(response.Headers.TryGetValues("X-Total-Count", out var total));
        Assert.Equal("2", total!.Single());
    }

    [Fact]
    public async Task Get_SemPageParam_NaoDeveEnviarLinkHeader()
    {
        var response = await _client.GetAsync("/v1/instruments", CancellationToken.None);

        Assert.False(response.Headers.Contains("Link"));
    }

    [Fact]
    public async Task Get_ComPaginacao_LinkHeaderTemRelsCorretosNaPrimeiraNoMeioENaUltimaPagina_EOrdemEEstavelEntrePaginas()
    {
        var sufixo = Guid.NewGuid().ToString("N");
        var ids = Enumerable.Range(0, 12).Select(i => $"td:instr-pag-{sufixo}-{i:D2}").ToList();
        foreach (var id in ids)
        {
            await CriarInstrumentoAsync(id);
        }

        var first = await _client.GetAsync(
            $"/v1/instruments?query={sufixo}&page=1&pageSize=5", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.TryGetValues("X-Total-Count", out var totalFirst));
        Assert.Equal("12", totalFirst!.Single());
        Assert.True(first.Headers.TryGetValues("Link", out var linkFirstValues));
        var linkFirst = linkFirstValues!.Single();
        Assert.Contains("rel=\"first\"", linkFirst, StringComparison.Ordinal);
        Assert.Contains("rel=\"next\"", linkFirst, StringComparison.Ordinal);
        Assert.Contains("rel=\"last\"", linkFirst, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"prev\"", linkFirst, StringComparison.Ordinal);

        var firstBody = await first.Content.ReadAsStringAsync(CancellationToken.None);
        using var firstDocument = JsonDocument.Parse(firstBody);
        var firstIds = firstDocument.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();

        var middle = await _client.GetAsync(
            $"/v1/instruments?query={sufixo}&page=2&pageSize=5", CancellationToken.None);
        Assert.True(middle.Headers.TryGetValues("Link", out var linkMiddleValues));
        var linkMiddle = linkMiddleValues!.Single();
        Assert.Contains("rel=\"first\"", linkMiddle, StringComparison.Ordinal);
        Assert.Contains("rel=\"prev\"", linkMiddle, StringComparison.Ordinal);
        Assert.Contains("rel=\"next\"", linkMiddle, StringComparison.Ordinal);
        Assert.Contains("rel=\"last\"", linkMiddle, StringComparison.Ordinal);

        var middleBody = await middle.Content.ReadAsStringAsync(CancellationToken.None);
        using var middleDocument = JsonDocument.Parse(middleBody);
        var middleIds = middleDocument.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();

        Assert.Empty(firstIds.Intersect(middleIds));

        var last = await _client.GetAsync(
            $"/v1/instruments?query={sufixo}&page=3&pageSize=5", CancellationToken.None);
        var lastBody = await last.Content.ReadAsStringAsync(CancellationToken.None);
        using var lastDocument = JsonDocument.Parse(lastBody);
        Assert.Equal(2, lastDocument.RootElement.GetArrayLength());
        Assert.True(last.Headers.TryGetValues("Link", out var linkLastValues));
        var linkLast = linkLastValues!.Single();
        Assert.Contains("rel=\"first\"", linkLast, StringComparison.Ordinal);
        Assert.Contains("rel=\"prev\"", linkLast, StringComparison.Ordinal);
        Assert.Contains("rel=\"last\"", linkLast, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"next\"", linkLast, StringComparison.Ordinal);

        var lastIds = lastDocument.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
        Assert.Empty(firstIds.Intersect(lastIds));
        Assert.Empty(middleIds.Intersect(lastIds));

        var todosOsIdsDevolvidos = firstIds.Concat(middleIds).Concat(lastIds).ToList();
        Assert.Equal(todosOsIdsDevolvidos.Distinct().Count(), todosOsIdsDevolvidos.Count);
        Assert.Equal<IEnumerable<string?>>(ids, todosOsIdsDevolvidos);
    }

    [Fact]
    public async Task Get_ComPageIntMaxValue_Retorna200ComCorpoVazioETotalCountELinkCoerentes()
    {
        var sufixo = Guid.NewGuid().ToString("N");
        await CriarInstrumentoAsync($"td:instr-pagemax-{sufixo}-a");
        await CriarInstrumentoAsync($"td:instr-pagemax-{sufixo}-b");

        var response = await _client.GetAsync(
            $"/v1/instruments?query={sufixo}&page={int.MaxValue}&pageSize=500", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(0, document.RootElement.GetArrayLength());

        Assert.True(response.Headers.TryGetValues("X-Total-Count", out var total));
        Assert.Equal("2", total!.Single());

        Assert.True(response.Headers.TryGetValues("Link", out var linkValues));
        var link = linkValues!.Single();
        Assert.Contains("rel=\"first\"", link, StringComparison.Ordinal);
        Assert.Contains("rel=\"last\"", link, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"next\"", link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_ComMaisDe100InstrumentsSemPage_CortaEm100EXTotalCountRefleteOTotal()
    {
        var sufixo = Guid.NewGuid().ToString("N");
        var ids = Enumerable.Range(0, 101).Select(i => $"td:instr-cem-{sufixo}-{i:D3}").ToList();
        foreach (var id in ids)
        {
            await CriarInstrumentoAsync(id);
        }

        var response = await _client.GetAsync($"/v1/instruments?query={sufixo}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Total-Count", out var total));
        Assert.Equal("101", total!.Single());

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(100, document.RootElement.GetArrayLength());
    }
}
