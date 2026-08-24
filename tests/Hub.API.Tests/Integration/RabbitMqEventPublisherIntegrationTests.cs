using System.Text;
using Hub.Domain.Common;
using Hub.Domain.Outbox;
using Hub.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Hub.API.Tests.Integration;

public sealed class RabbitMqEventPublisherIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3.13-management").Build();

    public Task InitializeAsync() => _rabbitMq.StartAsync();

    public Task DisposeAsync() => _rabbitMq.DisposeAsync().AsTask();

    private RabbitMqConnectionProvider CriarConnectionProvider(string exchange)
    {
        var uri = new Uri(_rabbitMq.GetConnectionString());
        var credenciais = uri.UserInfo.Split(':', 2);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:Host"] = uri.Host,
                ["RabbitMq:Port"] = uri.Port.ToString(),
                ["RabbitMq:User"] = credenciais[0],
                ["RabbitMq:Password"] = credenciais[1],
                ["RabbitMq:VirtualHost"] = "/",
                ["RabbitMq:Exchange"] = exchange
            })
            .Build();

        return new RabbitMqConnectionProvider(configuration, NullLogger<RabbitMqConnectionProvider>.Instance);
    }

    [Fact]
    public async Task ObterConexaoAsync_DeclaraExchangeComoTopicDuravelSemAutoDelete()
    {
        const string exchange = "prices-topologia";

        await using var connectionProvider = CriarConnectionProvider(exchange);
        var connection = await connectionProvider.ObterConexaoAsync(CancellationToken.None);

        await using (var channel = await connection.CreateChannelAsync())
        {
            var excecaoTipo = await Record.ExceptionAsync(() =>
                channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true, autoDelete: false));
            Assert.NotNull(excecaoTipo);
        }

        await using (var channel = await connection.CreateChannelAsync())
        {
            var excecaoDurable = await Record.ExceptionAsync(() =>
                channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: false, autoDelete: false));
            Assert.NotNull(excecaoDurable);
        }

        await using (var channel = await connection.CreateChannelAsync())
        {
            var excecaoAutoDelete = await Record.ExceptionAsync(() =>
                channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: true));
            Assert.NotNull(excecaoAutoDelete);
        }

        await using (var channel = await connection.CreateChannelAsync())
        {
            var excecaoParametrosCorretos = await Record.ExceptionAsync(() =>
                channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false));
            Assert.Null(excecaoParametrosCorretos);
        }
    }

    [Fact]
    public async Task PublicarAsync_MensagemChegaNaFilaComRoutingKeyPropriedadesEPayloadIntegros()
    {
        const string exchange = "prices-propriedades";
        const string routingKey = "precos.observado.propriedades";
        const string payload = """{"v":1,"tipo":"PrecoObservado","valor":"1234.56"}""";

        await using var connectionProvider = CriarConnectionProvider(exchange);
        var connection = await connectionProvider.ObterConexaoAsync(CancellationToken.None);

        await using var channel = await connection.CreateChannelAsync();
        var fila = await channel.QueueDeclareAsync(queue: "", durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(fila.QueueName, exchange, routingKey);

        var publisher = new RabbitMqEventPublisher(connectionProvider, NullLogger<RabbitMqEventPublisher>.Instance);
        var mensagem = new OutboxPendente(4242, "PrecoObservado", routingKey, payload);

        var resultado = await publisher.PublicarAsync([mensagem], CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(1, resultado.Value);

        var entrega = await channel.BasicGetAsync(fila.QueueName, autoAck: true);

        Assert.NotNull(entrega);
        Assert.Equal(routingKey, entrega!.RoutingKey);
        Assert.True(entrega.BasicProperties.Persistent);
        Assert.Equal("PrecoObservado", entrega.BasicProperties.Type);
        Assert.Equal("4242", entrega.BasicProperties.MessageId);
        Assert.Equal(payload, Encoding.UTF8.GetString(entrega.Body.Span));
    }

    [Fact]
    public async Task PublicarAsync_LoteDeVariasMensagens_DevolveTodasConfirmadas()
    {
        const string exchange = "prices-lote";
        const int quantidade = 5;

        await using var connectionProvider = CriarConnectionProvider(exchange);
        var connection = await connectionProvider.ObterConexaoAsync(CancellationToken.None);

        await using var channel = await connection.CreateChannelAsync();
        var fila = await channel.QueueDeclareAsync(queue: "", durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(fila.QueueName, exchange, "precos.observado.lote.#");

        var lote = Enumerable.Range(1, quantidade)
            .Select(i => new OutboxPendente(i, "PrecoObservado", $"precos.observado.lote.{i}", "{}"))
            .ToList();

        var publisher = new RabbitMqEventPublisher(connectionProvider, NullLogger<RabbitMqEventPublisher>.Instance);
        var resultado = await publisher.PublicarAsync(lote, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(quantidade, resultado.Value);

        var recebidas = 0;
        for (var i = 0; i < quantidade; i++)
        {
            var entrega = await channel.BasicGetAsync(fila.QueueName, autoAck: true);
            if (entrega is not null)
            {
                recebidas++;
            }
        }

        Assert.Equal(quantidade, recebidas);
    }

    [Fact]
    public async Task PublicarAsync_BrokerInacessivel_DevolveResultFailureBrokerIndisponivelSemLancar()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMq:Host"] = "127.0.0.1",
                ["RabbitMq:Port"] = "1",
                ["RabbitMq:User"] = "guest",
                ["RabbitMq:Password"] = "guest",
                ["RabbitMq:VirtualHost"] = "/",
                ["RabbitMq:Exchange"] = "prices-indisponivel"
            })
            .Build();

        await using var connectionProvider =
            new RabbitMqConnectionProvider(configuration, NullLogger<RabbitMqConnectionProvider>.Instance);
        var publisher = new RabbitMqEventPublisher(connectionProvider, NullLogger<RabbitMqEventPublisher>.Instance);

        var lote = new[] { new OutboxPendente(1, "PrecoObservado", "precos.observado.indisponivel", "{}") };

        Result<int>? resultado = null;
        var excecao = await Record.ExceptionAsync(async () =>
            resultado = await publisher.PublicarAsync(lote, CancellationToken.None));

        Assert.Null(excecao);
        Assert.NotNull(resultado);
        Assert.True(resultado!.IsFailure);
        Assert.Equal(OutboxErrors.BrokerIndisponivel.Code, resultado.Error.Code);
    }
}
