using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;
using Prometheus;
using Hub.API.Extensions;
using Hub.API.OpenApi;
using Hub.Infrastructure.Persistence;

namespace Hub.API;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>().ForwardToPrometheus();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddScoped<IDatabaseMigrator, EfCoreDatabaseMigrator>();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Hub API", Version = "v1" });

            var apiKeyScheme = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Api-Key",
                Description = "Chave de API obrigatória em todas as rotas de negócio.",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" },
            };
            c.AddSecurityDefinition("ApiKey", apiKeyScheme);
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [apiKeyScheme] = Array.Empty<string>(),
            });

            c.OperationFilter<PrecosAsOfOperationFilter>();
            c.OperationFilter<InstrumentsOperationFilter>();
        });

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                if (context.HttpContext.Items["CorrelationId"] is string correlationId)
                {
                    context.ProblemDetails.Extensions["correlationId"] = correlationId;
                }

                context.ProblemDetails.Extensions["traceId"] =
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            };
        });

        return services;
    }
}
