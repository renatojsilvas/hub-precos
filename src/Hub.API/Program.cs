using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Prometheus;
using Hub.API;
using Hub.API.Extensions;
using Hub.Application;
using Hub.Infrastructure;

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

app.Run();

public partial class Program;
