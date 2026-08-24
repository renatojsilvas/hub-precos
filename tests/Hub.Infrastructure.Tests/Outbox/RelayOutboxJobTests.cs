using Hub.Application.Outbox;
using Hub.Domain.Common;
using Hub.Infrastructure.Outbox;
using Hub.Infrastructure.Tests.Common;
using Hub.Infrastructure.Tests.Ingestao;
using Microsoft.Extensions.Logging;

namespace Hub.Infrastructure.Tests.Outbox;

public sealed class RelayOutboxJobTests
{
    private static readonly RelayResultado ResultadoSucesso = new(
        Lotes: 2,
        Publicados: 150,
        LotePartiu: false,
        PendentesRestantes: 10,
        IdadeMaisAntiga: TimeSpan.FromSeconds(30));

    [Fact]
    public async Task Execute_DespachaExatamenteUmPublicarOutboxCommand()
    {
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<RelayResultado>.Success(ResultadoSucesso)));
        var job = new RelayOutboxJob(sender, new FakeLogger<RelayOutboxJob>(), new FakeBusinessMetrics(), new RelayOutboxFalhaLogThrottle());
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        var requisicao = Assert.Single(sender.Requests);
        Assert.IsType<PublicarOutboxCommand>(requisicao.Request);
    }

    [Fact]
    public async Task Execute_RepassaOCancellationTokenDoContextoAoSender()
    {
        using var cts = new CancellationTokenSource();
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<RelayResultado>.Success(ResultadoSucesso)));
        var job = new RelayOutboxJob(sender, new FakeLogger<RelayOutboxJob>(), new FakeBusinessMetrics(), new RelayOutboxFalhaLogThrottle());
        var context = new FakeJobExecutionContext(cts.Token);

        await job.Execute(context);

        var requisicao = Assert.Single(sender.Requests);
        Assert.Equal(cts.Token, requisicao.CancellationToken);
    }

    [Fact]
    public async Task Execute_QuandoResultadoEhFalha_NaoLancaELogaErro()
    {
        var erro = new Error("Relay.Falha", "Falha ao publicar outbox.");
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<RelayResultado>.Failure(erro)));
        var logger = new FakeLogger<RelayOutboxJob>();
        var job = new RelayOutboxJob(sender, logger, new FakeBusinessMetrics(), new RelayOutboxFalhaLogThrottle());
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(erro.Code));
    }

    [Fact]
    public async Task Execute_QuandoResultadoEhSucesso_RegistraCicloEventosEBacklog()
    {
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<RelayResultado>.Success(ResultadoSucesso)));
        var metrics = new FakeBusinessMetrics();
        var job = new RelayOutboxJob(sender, new FakeLogger<RelayOutboxJob>(), metrics, new RelayOutboxFalhaLogThrottle());
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Equal(["success"], metrics.CiclosRelayRegistrados);
        Assert.Equal(ResultadoSucesso.Publicados, metrics.EventosPublicadosRegistrados);
        Assert.Contains(
            (ResultadoSucesso.PendentesRestantes!.Value, ResultadoSucesso.IdadeMaisAntiga!.Value.TotalSeconds),
            metrics.BacklogRegistrado);
    }

    [Fact]
    public async Task Execute_QuandoResultadoEhFalha_RegistraApenasCicloDeFalhaSemEventosNemBacklog()
    {
        var erro = new Error("Relay.Falha", "Falha ao publicar outbox.");
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<RelayResultado>.Failure(erro)));
        var metrics = new FakeBusinessMetrics();
        var job = new RelayOutboxJob(sender, new FakeLogger<RelayOutboxJob>(), metrics, new RelayOutboxFalhaLogThrottle());
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Equal(["failure"], metrics.CiclosRelayRegistrados);
        Assert.Equal(0, metrics.EventosPublicadosRegistrados);
        Assert.Empty(metrics.BacklogRegistrado);
    }

    [Fact]
    public async Task Execute_QuandoIdadeMaisAntigaEhNula_RegistraBacklogComIdadeZero()
    {
        var resultado = ResultadoSucesso with { PendentesRestantes = 0, IdadeMaisAntiga = null };
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<RelayResultado>.Success(resultado)));
        var metrics = new FakeBusinessMetrics();
        var job = new RelayOutboxJob(sender, new FakeLogger<RelayOutboxJob>(), metrics, new RelayOutboxFalhaLogThrottle());
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Contains((0L, 0d), metrics.BacklogRegistrado);
    }

    [Fact]
    public async Task Execute_QuandoPendentesRestantesEhNulo_NaoRegistraBacklog()
    {
        var resultado = ResultadoSucesso with { PendentesRestantes = null, IdadeMaisAntiga = null };
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<RelayResultado>.Success(resultado)));
        var metrics = new FakeBusinessMetrics();
        var job = new RelayOutboxJob(sender, new FakeLogger<RelayOutboxJob>(), metrics, new RelayOutboxFalhaLogThrottle());
        var context = new FakeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        Assert.Empty(metrics.BacklogRegistrado);
    }

    [Fact]
    public async Task Execute_QuandoCiclosConsecutivosFalham_LogaErroApenasNaTransicaoParaFalha()
    {
        var erro = new Error("Relay.Falha", "Falha ao publicar outbox.");
        var sender = new FakeSender((_, _) =>
            Task.FromResult<object?>(Result<RelayResultado>.Failure(erro)));
        var logger = new FakeLogger<RelayOutboxJob>();
        var throttle = new RelayOutboxFalhaLogThrottle();
        var metrics = new FakeBusinessMetrics();

        for (var i = 0; i < 5; i++)
        {
            var job = new RelayOutboxJob(sender, logger, metrics, throttle);
            await job.Execute(new FakeJobExecutionContext(CancellationToken.None));
        }

        Assert.Equal(1, logger.Entries.Count(e => e.Level == LogLevel.Error));
        Assert.Equal(5, metrics.CiclosRelayRegistrados.Count(o => o == "failure"));
    }

    [Fact]
    public async Task Execute_QuandoCicloSucedeAposFalhas_LogaRecuperacaoEVoltaALogarErroNaProximaFalha()
    {
        var erro = new Error("Relay.Falha", "Falha ao publicar outbox.");
        var falha = Result<RelayResultado>.Failure(erro);
        var sucesso = Result<RelayResultado>.Success(ResultadoSucesso);
        var logger = new FakeLogger<RelayOutboxJob>();
        var throttle = new RelayOutboxFalhaLogThrottle();
        var metrics = new FakeBusinessMetrics();

        var sender = new FakeSender((_, _) => Task.FromResult<object?>(falha));
        var job = new RelayOutboxJob(sender, logger, metrics, throttle);
        await job.Execute(new FakeJobExecutionContext(CancellationToken.None));
        await job.Execute(new FakeJobExecutionContext(CancellationToken.None));

        var senderSucesso = new FakeSender((_, _) => Task.FromResult<object?>(sucesso));
        var jobRecuperado = new RelayOutboxJob(senderSucesso, logger, metrics, throttle);
        await jobRecuperado.Execute(new FakeJobExecutionContext(CancellationToken.None));

        var senderFalhaNovamente = new FakeSender((_, _) => Task.FromResult<object?>(falha));
        var jobFalhaNovamente = new RelayOutboxJob(senderFalhaNovamente, logger, metrics, throttle);
        await jobFalhaNovamente.Execute(new FakeJobExecutionContext(CancellationToken.None));

        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Error));
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Information && e.Message.Contains("recuperado"));
    }
}
