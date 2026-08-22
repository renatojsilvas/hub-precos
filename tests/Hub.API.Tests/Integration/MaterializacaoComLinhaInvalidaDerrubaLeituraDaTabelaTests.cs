using Hub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class MaterializacaoComLinhaInvalidaDerrubaLeituraDaTabelaTests
{
    private readonly ApiTestFactory _factory;

    public MaterializacaoComLinhaInvalidaDerrubaLeituraDaTabelaTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Instrumentos_ComUmaLinhaDeIdSemPrefixoInseridaPorForaDoDominio_ExplodeALeituraDaTabelaInteira()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")!;
        await using (var conn = new NpgsqlConnection(connString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO instrumentos (id, classe, nome_exibicao, paga_cupom, metadados, criado_em)
                VALUES ('td:materializacao-linha-invalida-bom', 'td', 'Bom', false, '{}', now());

                INSERT INTO instrumentos (id, classe, nome_exibicao, paga_cupom, metadados, criado_em)
                VALUES (
                    'lixo-sem-prefixo-materializacao',
                    'lixo-sem-prefixo-materializacao',
                    'Corrompido',
                    false,
                    '{}',
                    now());
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Record.ExceptionAsync(() => db.Instrumentos.ToListAsync());

        Assert.NotNull(ex);
        Assert.IsType<InvalidOperationException>(ex);
    }
}
