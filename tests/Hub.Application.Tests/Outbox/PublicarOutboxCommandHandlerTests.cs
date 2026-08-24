using Hub.Application.Outbox;
using Hub.Application.Tests.Common;
using Hub.Domain.Common;
using Hub.Domain.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hub.Application.Tests.Outbox;

public sealed class PublicarOutboxCommandHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private static PublicarOutboxCommandHandler CriarHandler(
        FakeOutboxReadRepository read,
        FakeEventPublisher publisher,
        FakeOutboxWriteRepository write,
        IConfiguration? configuration = null,
        ILogger<PublicarOutboxCommandHandler>? logger = null) =>
        new(
            read,
            publisher,
            write,
            new FakeTimeProvider(Agora),
            configuration ?? new ConfigurationBuilder().Build(),
            logger ?? NullLogger<PublicarOutboxCommandHandler>.Instance);

    private static IConfiguration CriarConfiguracao(int? tamanhoLote = null, int? maxLotesPorCiclo = null)
    {
        var valores = new Dictionary<string, string?>();

        if (tamanhoLote is not null)
        {
            valores["Outbox:Relay:TamanhoLote"] = tamanhoLote.Value.ToString();
        }

        if (maxLotesPorCiclo is not null)
        {
            valores["Outbox:Relay:MaxLotesPorCiclo"] = maxLotesPorCiclo.Value.ToString();
        }

        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    private static IReadOnlyList<OutboxPendente> CriarLote(int quantidade, long idInicial = 1) =>
        Enumerable.Range(0, quantidade)
            .Select(i => new OutboxPendente(idInicial + i, "PrecoObservado", "precos.observado", "{}"))
            .ToList();

    [Fact]
    public async Task Handle_OutboxVazia_NaoPublicaNadaENaoAtualizaERetornaSucesso()
    {
        var read = new FakeOutboxReadRepository(
            backlogResultado: Result<BacklogOutbox>.Success(new BacklogOutbox(0, null)));
        var publisher = new FakeEventPublisher();
        var write = new FakeOutboxWriteRepository();

        var handler = CriarHandler(read, publisher, write);

        var resultado = await handler.Handle(new PublicarOutboxCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(0, resultado.Value.Lotes);
        Assert.Equal(0, resultado.Value.Publicados);
        Assert.False(resultado.Value.LotePartiu);
        Assert.Equal(0, resultado.Value.PendentesRestantes);
        Assert.Null(resultado.Value.IdadeMaisAntiga);
        Assert.Empty(publisher.LotesPublicados);
        Assert.Empty(write.ChamadasMarcarPublicados);
        Assert.True(read.BacklogChamado);
    }

    [Fact]
    public async Task Handle_LoteCheioSeguidoDeLoteParcial_DrenaEmDoisLotesPublicaEMarcaTudo()
    {
        var loteCheio = CriarLote(100, idInicial: 1);
        var loteParcial = CriarLote(40, idInicial: 101);

        var read = new FakeOutboxReadRepository(
            pendentesRespostas:
            [
                Result<IReadOnlyList<OutboxPendente>>.Success(loteCheio),
                Result<IReadOnlyList<OutboxPendente>>.Success(loteParcial)
            ],
            backlogResultado: Result<BacklogOutbox>.Success(new BacklogOutbox(0, null)));
        var publisher = new FakeEventPublisher();
        var write = new FakeOutboxWriteRepository();

        var handler = CriarHandler(read, publisher, write);

        var resultado = await handler.Handle(new PublicarOutboxCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(2, resultado.Value.Lotes);
        Assert.Equal(140, resultado.Value.Publicados);
        Assert.False(resultado.Value.LotePartiu);
        Assert.Equal(2, read.ChamadasObterPendentes);
        Assert.Equal(2, publisher.LotesPublicados.Count);
        Assert.Equal(2, write.ChamadasMarcarPublicados.Count);
        Assert.Equal(140, write.IdsMarcados.Count);
        Assert.Equal(
            Enumerable.Range(1, 140).Select(i => (long)i),
            write.IdsMarcados.OrderBy(id => id));
        Assert.All(write.InstantesRecebidos, instante => Assert.Equal(Agora, instante));
    }

    [Fact]
    public async Task Handle_PublisherConfirmaPrefixo_MarcaApenasConfirmadosEEncerraCicloComLotePartiu()
    {
        var lote = CriarLote(100, idInicial: 1);

        var read = new FakeOutboxReadRepository(
            pendentesRespostas: [Result<IReadOnlyList<OutboxPendente>>.Success(lote)],
            backlogResultado: Result<BacklogOutbox>.Success(new BacklogOutbox(60, TimeSpan.FromMinutes(3))));
        var publisher = new FakeEventPublisher(_ => Result<int>.Success(40));
        var write = new FakeOutboxWriteRepository();

        var handler = CriarHandler(read, publisher, write);

        var resultado = await handler.Handle(new PublicarOutboxCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.Lotes);
        Assert.Equal(40, resultado.Value.Publicados);
        Assert.True(resultado.Value.LotePartiu);
        Assert.Equal(1, read.ChamadasObterPendentes);
        Assert.Single(write.ChamadasMarcarPublicados);
        Assert.Equal(40, write.IdsMarcados.Count);
        Assert.Equal(Enumerable.Range(1, 40).Select(i => (long)i), write.IdsMarcados);
        Assert.DoesNotContain(41L, write.IdsMarcados);
        Assert.Equal(60, resultado.Value.PendentesRestantes);
    }

    [Fact]
    public async Task Handle_PublisherFalhaComZeroConfirmados_NadaEMarcadoECicloRetornaFalha()
    {
        var lote = CriarLote(10, idInicial: 1);

        var read = new FakeOutboxReadRepository(
            pendentesRespostas: [Result<IReadOnlyList<OutboxPendente>>.Success(lote)]);
        var publisher = new FakeEventPublisher(_ => Result<int>.Failure(OutboxErrors.BrokerIndisponivel));
        var write = new FakeOutboxWriteRepository();

        var handler = CriarHandler(read, publisher, write);

        var resultado = await handler.Handle(new PublicarOutboxCommand(), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OutboxErrors.BrokerIndisponivel.Code, resultado.Error.Code);
        Assert.Empty(write.ChamadasMarcarPublicados);
        Assert.False(read.BacklogChamado);
    }

    [Fact]
    public async Task Handle_MarcarPublicadosFalhaAposPublishConfirmado_RetornaFalhaSemMascarar()
    {
        var lote = CriarLote(10, idInicial: 1);

        var read = new FakeOutboxReadRepository(
            pendentesRespostas: [Result<IReadOnlyList<OutboxPendente>>.Success(lote)]);
        var publisher = new FakeEventPublisher();
        var write = new FakeOutboxWriteRepository
        {
            FalhaAoMarcar = Result<int>.Failure(OutboxErrors.FalhaAoMarcarPublicado)
        };

        var handler = CriarHandler(read, publisher, write);

        var resultado = await handler.Handle(new PublicarOutboxCommand(), CancellationToken.None);

        Assert.True(resultado.IsFailure);
        Assert.Equal(OutboxErrors.FalhaAoMarcarPublicado.Code, resultado.Error.Code);
        Assert.Single(write.ChamadasMarcarPublicados);
        Assert.Empty(write.IdsMarcados);
        Assert.False(read.BacklogChamado);
    }

    [Fact]
    public async Task Handle_TetoMaxLotesPorCicloRespeitado_NaoEntraEmLacoInfinito()
    {
        var lotes = Enumerable.Range(0, 5)
            .Select(i => Result<IReadOnlyList<OutboxPendente>>.Success(CriarLote(10, idInicial: 1 + (i * 10))))
            .ToList();

        var read = new FakeOutboxReadRepository(
            pendentesRespostas: lotes,
            backlogResultado: Result<BacklogOutbox>.Success(new BacklogOutbox(20, TimeSpan.FromMinutes(1))));
        var publisher = new FakeEventPublisher();
        var write = new FakeOutboxWriteRepository();

        var configuration = CriarConfiguracao(tamanhoLote: 10, maxLotesPorCiclo: 3);
        var handler = CriarHandler(read, publisher, write, configuration);

        var resultado = await handler.Handle(new PublicarOutboxCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(3, resultado.Value.Lotes);
        Assert.Equal(30, resultado.Value.Publicados);
        Assert.False(resultado.Value.LotePartiu);
        Assert.Equal(3, read.ChamadasObterPendentes);
        Assert.True(read.BacklogChamado);
    }

    [Fact]
    public async Task Handle_BacklogFalhaAposCicloProdutivo_RetornaSucessoComBacklogDesconhecidoEPublicadosPreservados()
    {
        var lote = CriarLote(10, idInicial: 1);

        var read = new FakeOutboxReadRepository(
            pendentesRespostas: [Result<IReadOnlyList<OutboxPendente>>.Success(lote)],
            backlogResultado: Result<BacklogOutbox>.Failure(
                OutboxErrors.FalhaDeLeitura("Falha ao obter backlog da outbox.")));
        var publisher = new FakeEventPublisher();
        var write = new FakeOutboxWriteRepository();
        var logger = new FakeLogger<PublicarOutboxCommandHandler>();

        var handler = CriarHandler(read, publisher, write, logger: logger);

        var resultado = await handler.Handle(new PublicarOutboxCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value.Lotes);
        Assert.Equal(10, resultado.Value.Publicados);
        Assert.False(resultado.Value.LotePartiu);
        Assert.Null(resultado.Value.PendentesRestantes);
        Assert.Null(resultado.Value.IdadeMaisAntiga);
        Assert.Equal(10, write.IdsMarcados.Count);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Handle_MarcarPublicadosAfetaMenosLinhasQueConfirmadas_PublicadosRefleteAfetadosELogaDivergencia()
    {
        var lote = CriarLote(10, idInicial: 1);

        var read = new FakeOutboxReadRepository(
            pendentesRespostas: [Result<IReadOnlyList<OutboxPendente>>.Success(lote)],
            backlogResultado: Result<BacklogOutbox>.Success(new BacklogOutbox(0, null)));
        var publisher = new FakeEventPublisher();
        var write = new FakeOutboxWriteRepository { AfetadosOverride = 7 };
        var logger = new FakeLogger<PublicarOutboxCommandHandler>();

        var handler = CriarHandler(read, publisher, write, logger: logger);

        var resultado = await handler.Handle(new PublicarOutboxCommand(), CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(7, resultado.Value.Publicados);
        Assert.Equal(7, write.IdsMarcados.Count);
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("Divergência"));
    }
}
