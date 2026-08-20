using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Prometheus;
using Hub.API;
using Hub.API.Extensions;
using Hub.Application;
using Hub.Domain.Common;
using Hub.Infrastructure;
using IResult = Microsoft.AspNetCore.Http.IResult;

var builder = WebApplication.CreateBuilder(args);

builder.AddSerilog();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices();

var app = builder.Build();

ConnectionStringGuard.Validate(app.Configuration, app.Environment);

await app.InitializeDatabaseAsync();

app.UseForwardedHeaders();

// UseHttpMetrics precisa envolver o UseExceptionHandler (não o contrário): o prometheus-net
// só lê o status code final da resposta no "finally" do seu próprio middleware, e é o
// UseExceptionHandler quem reescreve a resposta para 5xx quando um endpoint lança exceção.
// Se o UseHttpMetrics ficar por dentro (mais perto do endpoint), a exceção atravessa o seu
// try/finally antes do status ser reescrito, e o label `code` fica errado — mascarando
// incidentes. Ver tesouro-direto-api/src/TesouroDireto.API/Program.cs (mesma regra) e
// docs/PLANO.md daquele repo (tarefa 29).
var httpMetricsExcludedPaths = app.Configuration.GetSection("Metrics:ExcludedPaths").Get<string[]>() ?? [];
app.UseWhen(
    ctx => !httpMetricsExcludedPaths.Any(p =>
        ctx.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)),
    branch => branch.UseHttpMetrics());

app.UseExceptionHandler();

app.UseSerilogDefaults();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapMetrics();

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/_test/throw", IResult () => throw new InvalidOperationException("Forced exception for exception handler testing."))
        .ExcludeFromDescription();

    // Endpoints mínimos para exercitar ResultExtensions.ToHttpResult ponta a ponta (PADROES.md
    // §2: contrato problem+json com code/correlationId/traceId) sem inventar domínio de negócio.
    app.MapGet("/_test/result/validation", IResult () =>
            Result.Failure(new Error("Test.Validation", "Validation failure for testing.", ErrorType.Validation))
                .ToHttpResult(() => Results.Ok()))
        .ExcludeFromDescription();

    app.MapGet("/_test/result/not-found", IResult () =>
            Result.Failure(new Error("Test.NotFound", "Not found for testing.", ErrorType.NotFound))
                .ToHttpResult(() => Results.Ok()))
        .ExcludeFromDescription();

    app.MapGet("/_test/result/conflict", IResult () =>
            Result.Failure(new Error("Test.Conflict", "Conflict for testing.", ErrorType.Conflict))
                .ToHttpResult(() => Results.Ok()))
        .ExcludeFromDescription();

    app.MapGet("/_test/result/success", IResult () =>
            Result.Success().ToHttpResult(() => Results.Ok(new { ok = true })))
        .ExcludeFromDescription();
}

app.Run();

public partial class Program;
