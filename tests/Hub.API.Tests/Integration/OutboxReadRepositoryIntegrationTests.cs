using System.Text.Json.Nodes;
using Hub.Domain.Outbox;
using Hub.Infrastructure.Persistence;
using Hub.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Hub.API.Tests.Integration;

public sealed class OutboxReadRepositoryIntegrationTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private static async Task<OutboxMessage> CriarMensagemAsync(
        AppDbContext db, string tipo, string routingKey, string payload, DateTimeOffset criadoEm)
    {
        var mensagem = OutboxMessage.Create(tipo, routingKey, payload, criadoEm).Value;
        db.OutboxMessages.Add(mensagem);
        await db.SaveChangesAsync();
        return mensagem;
    }

    [Fact]
    public async Task ObterPendentesAsync_OrdenaPorIdCrescenteERespeitaOLimite()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dataSource).Options;
        await using (var migrationDb = new AppDbContext(options))
        {
            await migrationDb.Database.MigrateAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            for (var i = 0; i < 5; i++)
            {
                await CriarMensagemAsync(db, "PrecoObservado", $"precos.observado.{i}", "{}", Agora);
            }
        }

        var repo = new OutboxReadRepository(dataSource, TimeProvider.System, NullLogger<OutboxReadRepository>.Instance);

        var resultado = await repo.ObterPendentesAsync(3, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(3, resultado.Value.Count);
        Assert.Equal(resultado.Value.Select(m => m.Id).OrderBy(id => id), resultado.Value.Select(m => m.Id));
        Assert.Equal(
            ["precos.observado.0", "precos.observado.1", "precos.observado.2"],
            resultado.Value.Select(m => m.RoutingKey));
    }

    [Fact]
    public async Task ObterPendentesAsync_ExcluiMensagensJaPublicadas()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dataSource).Options;
        await using (var migrationDb = new AppDbContext(options))
        {
            await migrationDb.Database.MigrateAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            await CriarMensagemAsync(db, "PrecoObservado", "precos.observado.pendente-a", "{}", Agora);

            var publicada = await CriarMensagemAsync(db, "PrecoObservado", "precos.observado.publicada", "{}", Agora);
            publicada.MarcarPublicado(Agora.AddMinutes(1));
            await db.SaveChangesAsync();

            await CriarMensagemAsync(db, "PrecoObservado", "precos.observado.pendente-b", "{}", Agora);
        }

        var repo = new OutboxReadRepository(dataSource, TimeProvider.System, NullLogger<OutboxReadRepository>.Instance);

        var resultado = await repo.ObterPendentesAsync(10, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.DoesNotContain(resultado.Value, m => m.RoutingKey == "precos.observado.publicada");
        Assert.Contains(resultado.Value, m => m.RoutingKey == "precos.observado.pendente-a");
        Assert.Contains(resultado.Value, m => m.RoutingKey == "precos.observado.pendente-b");
    }

    [Fact]
    public async Task ObterPendentesAsync_PayloadChegaComoStringJsonIntegra()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dataSource).Options;
        await using (var migrationDb = new AppDbContext(options))
        {
            await migrationDb.Database.MigrateAsync();
        }

        const string payloadOriginal =
            """{"v":1,"tipo":"PrecoObservado","instrumentoId":"td:LTN-2030","valor":"1234.56","texto":"acentuação, aspas \" e emoji 🎯"}""";

        await using (var db = new AppDbContext(options))
        {
            await CriarMensagemAsync(db, "PrecoObservado", "precos.observado.payload", payloadOriginal, Agora);
        }

        var repo = new OutboxReadRepository(dataSource, TimeProvider.System, NullLogger<OutboxReadRepository>.Instance);

        var resultado = await repo.ObterPendentesAsync(10, CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        var mensagem = Assert.Single(resultado.Value);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(payloadOriginal), JsonNode.Parse(mensagem.Payload)),
            $"Payload divergiu após ida e volta pelo Postgres. Original: {payloadOriginal}; Obtido: {mensagem.Payload}");
    }

    [Fact]
    public async Task ObterBacklogAsync_ContaPendentesEDevolveIdadeDaMaisAntiga()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dataSource).Options;
        await using (var migrationDb = new AppDbContext(options))
        {
            await migrationDb.Database.MigrateAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            await CriarMensagemAsync(db, "PrecoObservado", "precos.observado.a", "{}", Agora.AddMinutes(-10));
            await CriarMensagemAsync(db, "PrecoObservado", "precos.observado.b", "{}", Agora.AddMinutes(-5));
            await CriarMensagemAsync(db, "PrecoObservado", "precos.observado.c", "{}", Agora.AddMinutes(-1));

            var publicadaAntiga = await CriarMensagemAsync(
                db, "PrecoObservado", "precos.observado.publicada-antiga", "{}", Agora.AddMinutes(-60));
            publicadaAntiga.MarcarPublicado(Agora.AddMinutes(-59));
            await db.SaveChangesAsync();
        }

        var timeProvider = new FakeTimeProvider(Agora);
        var repo = new OutboxReadRepository(dataSource, timeProvider, NullLogger<OutboxReadRepository>.Instance);

        var resultado = await repo.ObterBacklogAsync(CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(3, resultado.Value.Pendentes);
        Assert.Equal(TimeSpan.FromMinutes(10), resultado.Value.IdadeMaisAntiga);
    }

    [Fact]
    public async Task ObterBacklogAsync_SemMensagensPendentes_DevolveZeroEIdadeNula()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();

        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dataSource).Options;
        await using (var migrationDb = new AppDbContext(options))
        {
            await migrationDb.Database.MigrateAsync();
        }

        var repo = new OutboxReadRepository(dataSource, TimeProvider.System, NullLogger<OutboxReadRepository>.Instance);

        var resultado = await repo.ObterBacklogAsync(CancellationToken.None);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(0, resultado.Value.Pendentes);
        Assert.Null(resultado.Value.IdadeMaisAntiga);
    }
}
