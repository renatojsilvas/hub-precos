using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Hub.Infrastructure.Messaging;

public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private const string HostPadrao = "localhost";
    private const int PortaPadrao = 5672;
    private const string UsuarioPadrao = "guest";
    private const string SenhaPadrao = "guest";
    private const string VirtualHostPadrao = "/";
    private const string ExchangePadrao = "prices";
    private const string ClientProvidedName = "hub-precos-relay";

    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _host;
    private readonly int _port;
    private readonly string _usuario;
    private readonly string _senha;
    private readonly string _virtualHost;
    private IConnection? _connection;
    private bool? _ultimaConexaoOk;

    public RabbitMqConnectionProvider(IConfiguration configuration, ILogger<RabbitMqConnectionProvider> logger)
    {
        _logger = logger;
        _host = ValorOuPadrao(configuration["RabbitMq:Host"], HostPadrao);
        _port = ResolverPorta(configuration["RabbitMq:Port"]);
        _usuario = ValorOuPadrao(configuration["RabbitMq:User"], UsuarioPadrao);
        _senha = ValorOuPadrao(configuration["RabbitMq:Password"], SenhaPadrao);
        _virtualHost = ValorOuPadrao(configuration["RabbitMq:VirtualHost"], VirtualHostPadrao);
        Exchange = ValorOuPadrao(configuration["RabbitMq:Exchange"], ExchangePadrao);
    }

    public string Exchange { get; }

    public async Task<IConnection> ObterConexaoAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = _host,
                    Port = _port,
                    UserName = _usuario,
                    Password = _senha,
                    VirtualHost = _virtualHost,
                    AutomaticRecoveryEnabled = true,
                    TopologyRecoveryEnabled = true,
                    ClientProvidedName = ClientProvidedName
                };

                var connection = await factory.CreateConnectionAsync(ct);

                await using (var channel = await connection.CreateChannelAsync(cancellationToken: ct))
                {
                    await channel.ExchangeDeclareAsync(
                        Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
                }

                _connection = connection;
                RegistrarSucesso();

                return connection;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                RegistrarFalha(ex);
                throw;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void RegistrarSucesso()
    {
        if (_ultimaConexaoOk != true)
        {
            _logger.LogInformation("Conexao com o RabbitMQ estabelecida em {Host}:{Port}.", _host, _port);
        }

        _ultimaConexaoOk = true;
    }

    private void RegistrarFalha(Exception ex)
    {
        if (_ultimaConexaoOk != false)
        {
            _logger.LogError(ex, "Falha ao conectar ao RabbitMQ em {Host}:{Port}.", _host, _port);
        }

        _ultimaConexaoOk = false;
    }

    private static string ValorOuPadrao(string? valor, string padrao) =>
        string.IsNullOrWhiteSpace(valor) ? padrao : valor;

    private static int ResolverPorta(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return PortaPadrao;
        }

        return int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var porta)
            ? porta
            : PortaPadrao;
    }

    public async ValueTask DisposeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        finally
        {
            _semaphore.Release();
        }

        _semaphore.Dispose();
    }
}
