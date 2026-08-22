using System.Text.Json.Nodes;
using Hub.Domain.Fontes;
using Hub.Domain.Instrumentos;
using Hub.Domain.Precos;
using Hub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class EfRoundTripTests
{
    private readonly ApiTestFactory _factory;

    public EfRoundTripTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Instrumento_SalvoERecarregadoPeloEf_PreservaTodosOsCampos()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var id = InstrumentoId.Create("td:round-trip-instrumento").Value;
        var criadoEm = new DateTimeOffset(2026, 8, 20, 10, 30, 0, TimeSpan.Zero);
        var instrumento = Instrumento.Create(
            id,
            nomeExibicao: "Round trip",
            ativoDesde: new DateOnly(2026, 1, 1),
            ativoAte: new DateOnly(2030, 1, 1),
            pagaCupom: true,
            metadados: """{"chave":"valor"}""",
            criadoEm: criadoEm).Value;

        db.Instrumentos.Add(instrumento);
        await db.SaveChangesAsync();

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var lido = await db2.Instrumentos.SingleAsync(i => i.Id == id);

        Assert.Equal(instrumento.Id, lido.Id);
        Assert.Equal(instrumento.Classe, lido.Classe);
        Assert.Equal(instrumento.NomeExibicao, lido.NomeExibicao);
        Assert.Equal(instrumento.AtivoDesde, lido.AtivoDesde);
        Assert.Equal(instrumento.AtivoAte, lido.AtivoAte);
        Assert.Equal(instrumento.PagaCupom, lido.PagaCupom);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(instrumento.Metadados), JsonNode.Parse(lido.Metadados)),
            $"metadados divergiu no round-trip: salvo '{instrumento.Metadados}', lido '{lido.Metadados}' " +
            "(jsonb do Postgres reformata o texto, então a comparação é estrutural, não literal).");
        Assert.Equal(instrumento.CriadoEm, lido.CriadoEm);
    }

    [Fact]
    public async Task InstrumentoFonte_SalvaERecarregadaPeloEf_PreservaTodosOsCampos()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var instrumentoId = InstrumentoId.Create("td:round-trip-fonte").Value;
        var instrumento = Instrumento.Create(
            instrumentoId,
            nomeExibicao: "Round trip fonte",
            ativoDesde: null,
            ativoAte: null,
            pagaCupom: false,
            metadados: "{}",
            criadoEm: DateTimeOffset.UtcNow).Value;

        db.Instrumentos.Add(instrumento);
        await db.SaveChangesAsync();

        var fonte = Fonte.Create("td-api").Value;
        var codigoNaFonte = CodigoNaFonte.Create("round-trip-fonte-codigo").Value;
        var instrumentoFonte = InstrumentoFonte.Create(instrumentoId, fonte, codigoNaFonte).Value;

        db.InstrumentoFontes.Add(instrumentoFonte);
        await db.SaveChangesAsync();

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var lida = await db2.InstrumentoFontes
            .SingleAsync(f => f.InstrumentoId == instrumentoId && f.Fonte == fonte);

        Assert.Equal(instrumentoId, lida.InstrumentoId);
        Assert.Equal(fonte, lida.Fonte);
        Assert.Equal(codigoNaFonte, lida.CodigoNaFonte);
    }

    [Fact]
    public async Task Preco_SalvoERecarregadoPeloEf_PreservaTodosOsCampos()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var instrumentoId = InstrumentoId.Create("td:round-trip-preco").Value;
        var instrumento = Instrumento.Create(
            instrumentoId,
            nomeExibicao: "Round trip preco",
            ativoDesde: null,
            ativoAte: null,
            pagaCupom: false,
            metadados: "{}",
            criadoEm: DateTimeOffset.UtcNow).Value;

        db.Instrumentos.Add(instrumento);
        await db.SaveChangesAsync();

        var dataRef = DataRef.Create(new DateOnly(2026, 8, 20)).Value;
        var campo = Campo.Create("pu_venda").Value;
        var fonte = Fonte.Create("td-api").Value;
        var observadoEm = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var preco = Preco.Create(
            instrumentoId,
            dataRef,
            campo,
            fonte,
            valor: 15000.123456m,
            revisao: 0,
            observadoEm: observadoEm).Value;

        db.Precos.Add(preco);
        await db.SaveChangesAsync();

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var lido = await db2.Precos.SingleAsync(p =>
            p.InstrumentoId == instrumentoId
            && p.DataRef == dataRef
            && p.Campo == campo
            && p.Fonte == fonte
            && p.Revisao == 0);

        Assert.Equal(preco.InstrumentoId, lido.InstrumentoId);
        Assert.Equal(preco.DataRef, lido.DataRef);
        Assert.Equal(preco.Campo, lido.Campo);
        Assert.Equal(preco.Fonte, lido.Fonte);
        Assert.Equal(preco.Valor, lido.Valor);
        Assert.Equal(preco.Revisao, lido.Revisao);
        Assert.Equal(preco.ObservadoEm, lido.ObservadoEm);
    }
}
