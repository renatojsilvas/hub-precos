using System.Runtime.CompilerServices;
using System.Text.Json;
using Hub.Application.Adapters;
using Hub.Application.Common.Interfaces;
using Hub.Application.Ingestao;
using Hub.Application.Outbox;
using Hub.Application.Precos;
using Hub.Domain.Common;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Outbox;
using Hub.Domain.Precos;
using Hub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class IngerirPrecosTdCommandHandlerIntegrationTests
{
    private readonly ApiTestFactory _factory;

    public IngerirPrecosTdCommandHandlerIntegrationTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    private static readonly DateTimeOffset Agora = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakePriceSourceAdapter(
        DiscoveryResultado discoveryResultado,
        Func<string, DateOnly, IReadOnlyList<PriceObserved>> fetchFactory) : IPriceSourceAdapter
    {
        public string Fonte => "td-api";

        public Task<Result<DiscoveryResultado>> DiscoverAsync(CancellationToken ct) =>
            Task.FromResult(Result<DiscoveryResultado>.Success(discoveryResultado));

        public async IAsyncEnumerable<PrecoLido> FetchAsync(
            string codigoNaFonte, DateOnly dataInicio, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();

            var linha = 0;
            foreach (var item in fetchFactory(codigoNaFonte, dataInicio))
            {
                linha++;
                yield return new PrecoLido(linha, item);
            }
        }
    }

    private static IngerirPrecosTdCommandHandler CriarHandler(
        IServiceProvider services, IPriceSourceAdapter adapter) =>
        new(
            adapter,
            services.GetRequiredService<IIngestaoReadRepository>(),
            services.GetRequiredService<IPrecoWriteRepository>(),
            services.GetRequiredService<IOutboxWriteRepository>(),
            services.GetRequiredService<IUnitOfWork>(),
            new FakeTimeProvider(Agora),
            services.GetRequiredService<IConfiguration>(),
            NullLogger<IngerirPrecosTdCommandHandler>.Instance);

    private async Task<InstrumentoId> CriarInstrumentoComFonteAsync(
        AppDbContext db, string sufixo, string codigoNaFonte)
    {
        var id = InstrumentoId.Create($"td:{sufixo}").Value;

        var instrumento = Instrumento.Create(
            id, sufixo, ativoDesde: null, ativoAte: null, pagaCupom: false, metadados: Metadados.Create("{}").Value, criadoEm: Agora).Value;
        db.Instrumentos.Add(instrumento);

        var fonte = InstrumentoFonte.Create(
            id, Fonte.Create("td-api").Value, CodigoNaFonte.Create(codigoNaFonte).Value).Value;
        db.InstrumentoFontes.Add(fonte);

        await db.SaveChangesAsync();

        return id;
    }

    [Fact]
    public async Task Handle_InstrumentoSemPreco_PopulaCanonicaComOutboxVazia_EEhIdempotenteNaSegundaExecucaoComContextoNovo()
    {
        var sufixo = $"ciclo-idempotente-{Guid.NewGuid():N}";
        var codigo = $"codigo-{sufixo}";
        var dataRef = new DateOnly(2026, 8, 10);

        InstrumentoId instrumentoId;
        using (var scopeSetup = _factory.Services.CreateScope())
        {
            var db = scopeSetup.ServiceProvider.GetRequiredService<AppDbContext>();
            instrumentoId = await CriarInstrumentoComFonteAsync(db, sufixo, codigo);
        }

        IReadOnlyList<PriceObserved> Precos(string cod, DateOnly _) =>
            cod == codigo
                ? [new PriceObserved(
                    instrumentoId, DataRef.Create(dataRef).Value, Campo.Create(Campos.PuVenda).Value,
                    Fonte.Create("td-api").Value, 100.5m, Agora)]
                : [];

        using (var scope1 = _factory.Services.CreateScope())
        {
            var handler1 = CriarHandler(scope1.ServiceProvider, new FakePriceSourceAdapter(
                new DiscoveryResultado(true, 0, 0, 0), Precos));

            var resultado1 = await handler1.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

            Assert.True(resultado1.IsSuccess);
            Assert.Equal(1, resultado1.Value.PrecosInseridos);
        }

        using (var verify1 = _factory.Services.CreateScope())
        {
            var db = verify1.ServiceProvider.GetRequiredService<AppDbContext>();

            var precos = await db.Precos.Where(p => p.InstrumentoId == instrumentoId).ToListAsync();
            var preco = Assert.Single(precos);
            Assert.Equal(100.5m, preco.Valor);
            Assert.Equal(0, preco.Revisao);

            var eventosDoInstrumento = (await db.OutboxMessages
                .Where(o => o.Tipo == "PriceObserved")
                .ToListAsync())
                .Where(o => o.Payload.Contains(instrumentoId.Value, StringComparison.Ordinal))
                .ToList();
            Assert.Empty(eventosDoInstrumento);
        }

        using (var scope2 = _factory.Services.CreateScope())
        {
            var handler2 = CriarHandler(scope2.ServiceProvider, new FakePriceSourceAdapter(
                new DiscoveryResultado(true, 0, 0, 0), Precos));

            var resultado2 = await handler2.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

            Assert.True(resultado2.IsSuccess);
            Assert.Equal(0, resultado2.Value.PrecosInseridos);
            Assert.Equal(0, resultado2.Value.PrecosRevisados);
            Assert.True(resultado2.Value.PrecosInalterados >= 1);
        }

        using (var verify2 = _factory.Services.CreateScope())
        {
            var db = verify2.ServiceProvider.GetRequiredService<AppDbContext>();

            var precos = await db.Precos.Where(p => p.InstrumentoId == instrumentoId).ToListAsync();
            Assert.Single(precos);

            var eventosDoInstrumento = (await db.OutboxMessages
                .Where(o => o.Tipo == "PriceObserved")
                .ToListAsync())
                .Where(o => o.Payload.Contains(instrumentoId.Value, StringComparison.Ordinal))
                .ToList();
            Assert.Empty(eventosDoInstrumento);
        }
    }

    [Fact]
    public async Task Handle_SegundoCicloComDiaNovoNaJanelaRecente_GravaUmaLinhaEUmEventoComPayloadDaSecao51()
    {
        var sufixo = $"ciclo-dia-novo-{Guid.NewGuid():N}";
        var codigo = $"codigo-{sufixo}";
        var dataAntiga = new DateOnly(2026, 8, 10);
        var dataNova = new DateOnly(2026, 8, 20);

        InstrumentoId instrumentoId;
        using (var scopeSetup = _factory.Services.CreateScope())
        {
            var db = scopeSetup.ServiceProvider.GetRequiredService<AppDbContext>();
            instrumentoId = await CriarInstrumentoComFonteAsync(db, sufixo, codigo);
        }

        using (var scope1 = _factory.Services.CreateScope())
        {
            IReadOnlyList<PriceObserved> PrecosCiclo1(string cod, DateOnly _) =>
                cod == codigo
                    ? [new PriceObserved(
                        instrumentoId, DataRef.Create(dataAntiga).Value, Campo.Create(Campos.PuVenda).Value,
                        Fonte.Create("td-api").Value, 100.5m, Agora)]
                    : [];

            var handler1 = CriarHandler(scope1.ServiceProvider, new FakePriceSourceAdapter(
                new DiscoveryResultado(true, 0, 0, 0), PrecosCiclo1));

            await handler1.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);
        }

        using (var scope2 = _factory.Services.CreateScope())
        {
            IReadOnlyList<PriceObserved> PrecosCiclo2(string cod, DateOnly _) =>
                cod == codigo
                    ?
                    [
                        new PriceObserved(
                            instrumentoId, DataRef.Create(dataAntiga).Value, Campo.Create(Campos.PuVenda).Value,
                            Fonte.Create("td-api").Value, 100.5m, Agora),
                        new PriceObserved(
                            instrumentoId, DataRef.Create(dataNova).Value, Campo.Create(Campos.PuVenda).Value,
                            Fonte.Create("td-api").Value, 200.75m, Agora),
                    ]
                    : [];

            var handler2 = CriarHandler(scope2.ServiceProvider, new FakePriceSourceAdapter(
                new DiscoveryResultado(true, 0, 0, 0), PrecosCiclo2));

            var resultado2 = await handler2.Handle(new IngerirPrecosTdCommand(), CancellationToken.None);

            Assert.True(resultado2.IsSuccess);
            Assert.Equal(1, resultado2.Value.PrecosInseridos);
        }

        using var verify = _factory.Services.CreateScope();
        var verificaDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var precos = await verificaDb.Precos.Where(p => p.InstrumentoId == instrumentoId).ToListAsync();
        Assert.Equal(2, precos.Count);
        Assert.Contains(precos, p => p.DataRef.Value == dataNova && p.Valor == 200.75m);

        var eventos = (await verificaDb.OutboxMessages
            .Where(o => o.Tipo == "PriceObserved")
            .ToListAsync())
            .Where(o => o.Payload.Contains(instrumentoId.Value, StringComparison.Ordinal))
            .ToList();

        var evento = Assert.Single(eventos);
        Assert.Equal("prices.td", evento.RoutingKey);

        using var payload = JsonDocument.Parse(evento.Payload);
        var raiz = payload.RootElement;

        Assert.Equal(1, raiz.GetProperty("v").GetInt32());
        Assert.Equal("PriceObserved", raiz.GetProperty("tipo").GetString());
        Assert.Equal(instrumentoId.Value, raiz.GetProperty("instrumentoId").GetString());
        Assert.Equal("2026-08-20", raiz.GetProperty("dataRef").GetString());
        Assert.Equal("pu_venda", raiz.GetProperty("campo").GetString());
        Assert.Equal("200.75", raiz.GetProperty("valor").GetString());
        Assert.Equal("td-api", raiz.GetProperty("fonte").GetString());
        Assert.Equal(0, raiz.GetProperty("revisao").GetInt32());
        Assert.True(raiz.TryGetProperty("observadoEm", out _));
    }

    [Fact]
    public async Task AdicionarEodSeAusenteAsync_RespeitaOIndiceUnicoSemEstourarErroParaOChamador()
    {
        using var scope = _factory.Services.CreateScope();
        var outboxWrite = scope.ServiceProvider.GetRequiredService<IOutboxWriteRepository>();

        var offsetDias = Math.Abs(Guid.NewGuid().GetHashCode()) % 300;
        var data = new DateOnly(2999, 1, 1).AddDays(offsetDias).ToString("yyyy-MM-dd");
        var payload = $"{{\"v\":1,\"tipo\":\"EodPricesReady\",\"data\":\"{data}\",\"classes\":[\"td\"]}}";

        var mensagem1 = OutboxMessage.Create("EodPricesReady", "eod.ready", payload, Agora).Value;
        var resultado1 = await outboxWrite.AdicionarEodSeAusenteAsync(mensagem1, CancellationToken.None);
        Assert.True(resultado1.IsSuccess);

        var mensagem2 = OutboxMessage.Create("EodPricesReady", "eod.ready", payload, Agora).Value;

        Result? resultado2 = null;
        var excecao = await Record.ExceptionAsync(async () =>
            resultado2 = await outboxWrite.AdicionarEodSeAusenteAsync(mensagem2, CancellationToken.None));

        Assert.Null(excecao);
        Assert.NotNull(resultado2);
        Assert.True(resultado2!.IsFailure);
        Assert.Equal(OutboxErrors.EodJaEmitido.Code, resultado2.Error.Code);

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var candidatos = await db.OutboxMessages.Where(o => o.Tipo == "EodPricesReady").ToListAsync();
        var quantidade = candidatos.Count(o => o.Payload.Contains(data, StringComparison.Ordinal));
        Assert.Equal(1, quantidade);
    }
}
