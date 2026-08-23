using Hub.Application.Ingestao;
using Hub.Domain.Common;
using Hub.Infrastructure.Ingestao;
using Hub.Infrastructure.Tests.Common;
using Microsoft.Extensions.Logging;

namespace Hub.Infrastructure.Tests.Ingestao;

public sealed class TdIngestaoJobTests
{
    private static readonly IngestaoResultado ResultadoSucesso = new(
        CicloEncerradoNaSonda: true,
        InstrumentosBackfill: 1,
        InstrumentosDelta: 2,
        InstrumentosPulados: 0,
        InstrumentosComFalha: 0,
        PrecosInseridos: 10,
        PrecosRevisados: 1,
        PrecosInalterados: 3,
        PrecosRejeitados: 0,
        LinhasComErro: 0,
        EventosGerados: 4,
        EodEmitido: new DateOnly(2026, 8, 21));

    [Fact]
    public async Task Execute_DespachaExatamenteUmIngerirPrecosTdCommand()
    {
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<IngestaoResultado>.Success(ResultadoSucesso)));
        var job = new TdIngestaoJob(sender, new FakeLogger<TdIngestaoJob>(), new FakeBusinessMetrics());
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        var requisicao = Assert.Single(sender.Requests);
        Assert.IsType<IngerirPrecosTdCommand>(requisicao.Request);
    }

    [Fact]
    public async Task Execute_QuandoResultadoEhFalha_NaoLancaELogaErro()
    {
        var erro = new Error("Ingestao.Falha", "Falha ao ingerir precos.");
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<IngestaoResultado>.Failure(erro)));
        var logger = new FakeLogger<TdIngestaoJob>();
        var job = new TdIngestaoJob(sender, logger, new FakeBusinessMetrics());
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(erro.Code));
    }

    [Fact]
    public async Task Execute_RepassaOCancellationTokenDoContextoAoSender()
    {
        using var cts = new CancellationTokenSource();
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<IngestaoResultado>.Success(ResultadoSucesso)));
        var job = new TdIngestaoJob(sender, new FakeLogger<TdIngestaoJob>(), new FakeBusinessMetrics());
        var context = new FakeJobExecutionContext(cts.Token);

        await job.Execute(context);

        var requisicao = Assert.Single(sender.Requests);
        Assert.Equal(cts.Token, requisicao.CancellationToken);
    }

    [Fact]
    public async Task Execute_QuandoResultadoEhSucesso_RegistraMetricasDeCicloEDePrecosPorTipo()
    {
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<IngestaoResultado>.Success(ResultadoSucesso)));
        var metrics = new FakeBusinessMetrics();
        var job = new TdIngestaoJob(sender, new FakeLogger<TdIngestaoJob>(), metrics);
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Equal(["success"], metrics.CiclosRegistrados);
        Assert.Contains(("inserido", (long)ResultadoSucesso.PrecosInseridos), metrics.PrecosRegistrados);
        Assert.Contains(("revisado", (long)ResultadoSucesso.PrecosRevisados), metrics.PrecosRegistrados);
        Assert.Contains(("inalterado", (long)ResultadoSucesso.PrecosInalterados), metrics.PrecosRegistrados);
        Assert.Contains(("rejeitado", (long)ResultadoSucesso.PrecosRejeitados), metrics.PrecosRegistrados);
    }

    [Fact]
    public async Task Execute_QuandoResultadoEhFalha_RegistraApenasCicloDeFalhaSemMetricasDePrecos()
    {
        var erro = new Error("Ingestao.Falha", "Falha ao ingerir precos.");
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<IngestaoResultado>.Failure(erro)));
        var metrics = new FakeBusinessMetrics();
        var job = new TdIngestaoJob(sender, new FakeLogger<TdIngestaoJob>(), metrics);
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Equal(["failure"], metrics.CiclosRegistrados);
        Assert.Empty(metrics.PrecosRegistrados);
        Assert.Equal(0, metrics.IngestaoSucessoRegistrada);
        Assert.Equal(0, metrics.PrecoNovoRegistrado);
        Assert.Empty(metrics.InstrumentosComFalhaRegistrados);
    }

    [Fact]
    public async Task Execute_QuandoResultadoEhSucesso_RegistraTimestampDeSucesso()
    {
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<IngestaoResultado>.Success(ResultadoSucesso)));
        var metrics = new FakeBusinessMetrics();
        var job = new TdIngestaoJob(sender, new FakeLogger<TdIngestaoJob>(), metrics);
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Equal(1, metrics.IngestaoSucessoRegistrada);
    }

    [Fact]
    public async Task Execute_QuandoHaPrecosInseridosOuRevisados_RegistraTimestampDePrecoNovo()
    {
        var resultado = ResultadoSucesso with { PrecosInseridos = 1, PrecosRevisados = 0 };
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<IngestaoResultado>.Success(resultado)));
        var metrics = new FakeBusinessMetrics();
        var job = new TdIngestaoJob(sender, new FakeLogger<TdIngestaoJob>(), metrics);
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Equal(1, metrics.PrecoNovoRegistrado);
    }

    [Fact]
    public async Task Execute_QuandoSoHaPrecosRevisadosSemInseridos_RegistraTimestampDePrecoNovo()
    {
        var resultado = ResultadoSucesso with { PrecosInseridos = 0, PrecosRevisados = 1 };
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<IngestaoResultado>.Success(resultado)));
        var metrics = new FakeBusinessMetrics();
        var job = new TdIngestaoJob(sender, new FakeLogger<TdIngestaoJob>(), metrics);
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Equal(1, metrics.PrecoNovoRegistrado);
    }

    [Fact]
    public async Task Execute_QuandoNaoHaPrecosInseridosNemRevisados_NaoRegistraTimestampDePrecoNovo()
    {
        var resultado = ResultadoSucesso with { PrecosInseridos = 0, PrecosRevisados = 0 };
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<IngestaoResultado>.Success(resultado)));
        var metrics = new FakeBusinessMetrics();
        var job = new TdIngestaoJob(sender, new FakeLogger<TdIngestaoJob>(), metrics);
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Equal(0, metrics.PrecoNovoRegistrado);
    }

    [Fact]
    public async Task Execute_QuandoResultadoEhSucesso_RegistraInstrumentosComFalha()
    {
        var resultado = ResultadoSucesso with { InstrumentosComFalha = 2 };
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<IngestaoResultado>.Success(resultado)));
        var metrics = new FakeBusinessMetrics();
        var job = new TdIngestaoJob(sender, new FakeLogger<TdIngestaoJob>(), metrics);
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Equal([2L], metrics.InstrumentosComFalhaRegistrados);
    }
}
