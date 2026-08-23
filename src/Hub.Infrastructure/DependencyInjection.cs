using Hub.Application.Adapters;
using Hub.Application.Common.Interfaces;
using Hub.Application.Ingestao;
using Hub.Application.Instrumentos;
using Hub.Application.Outbox;
using Hub.Application.Precos;
using Hub.Infrastructure.Caching;
using Hub.Infrastructure.Http;
using Hub.Infrastructure.Ingestao;
using Hub.Infrastructure.Observability;
using Hub.Infrastructure.Persistence;
using Hub.Infrastructure.Persistence.Repositories;
using Hub.Infrastructure.TdApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Npgsql;
using Polly;
using Polly.RateLimiting;
using Prometheus;
using Quartz;

namespace Hub.Infrastructure;

public static class DependencyInjection
{
    private const int NpgsqlMaxPoolSize = 5;
    private const long MemoryCacheSizeLimit = 1_000;

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(
            configuration.GetConnectionString("DefaultConnection")!)
        {
            NoResetOnClose = true,
            MaxPoolSize = NpgsqlMaxPoolSize
        }.ConnectionString;

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IInstrumentoWriteRepository, InstrumentoWriteRepository>();
        services.AddScoped<IInstrumentoReadRepository, InstrumentoReadRepository>();
        services.AddScoped<IPriceSourceAdapter, TdApiAdapter>();
        services.AddScoped<IIngestaoReadRepository, IngestaoReadRepository>();
        services.AddScoped<IPrecoWriteRepository, PrecoWriteRepository>();
        services.AddScoped<IOutboxWriteRepository, OutboxWriteRepository>();
        services.AddScoped<IPrecosAsOfReadRepository, PrecosAsOfReadRepository>();

        services.AddMemoryCache(options => options.SizeLimit = MemoryCacheSizeLimit);

        services.AddScoped<ContentVersionProvider>();
        services.AddScoped<IContentVersionProvider>(sp =>
            new CachedContentVersionProvider(
                sp.GetRequiredService<ContentVersionProvider>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<IBusinessMetrics, BusinessMetrics>();

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IConditionalGetStore, BoundedConditionalGetStore>();

        services.AddHttpClient<ITdApiClient, TdApiClient>(client =>
        {
            var baseUrl = configuration["TdApi:BaseUrl"];
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
                && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps))
            {
                client.BaseAddress = baseUri;
            }

            var apiKey = configuration["TdApi:ApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            }

            client.Timeout = TimeSpan.FromSeconds(60);
        })
        .UseHttpClientMetrics()
        .AddTdApiResilienceHandler(configuration);

        var agendamentoAtivo = configuration.GetValue<bool?>("TdApi:AgendamentoAtivo") ?? true;
        var cron = configuration["TdApi:CronSchedule"] ?? "0 0/15 * * * ?";

        services.AddQuartz(q =>
        {
            if (!agendamentoAtivo)
            {
                return;
            }

            var jobKey = new JobKey("td-ingestao");
            q.AddJob<TdIngestaoJob>(opts => opts.WithIdentity(jobKey));
            q.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithIdentity("td-ingestao-trigger")
                .WithCronSchedule(cron, x => x.WithMisfireHandlingInstructionDoNothing()));
        });
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }

    public static IHttpResiliencePipelineBuilder AddTdApiResilienceHandler(
        this IHttpClientBuilder builder, IConfiguration configuration)
    {
        return builder.AddResilienceHandler("td-api-resilience", pipeline =>
        {
            var section = configuration.GetSection("Resilience:TdApi");

            var totalTimeout = section.GetValue<TimeSpan?>("TotalTimeout") ?? TimeSpan.FromSeconds(50);
            var retryMaxAttempts = section.GetValue<int?>("Retry:MaxAttempts") ?? 3;
            var retryBaseDelay = section.GetValue<TimeSpan?>("Retry:BaseDelay") ?? TimeSpan.FromSeconds(0.5);
            var failureRatio = section.GetValue<double?>("CircuitBreaker:FailureRatio") ?? 0.5;
            var minimumThroughput = section.GetValue<int?>("CircuitBreaker:MinimumThroughput") ?? 10;
            var samplingDuration = section.GetValue<TimeSpan?>("CircuitBreaker:SamplingDuration") ?? TimeSpan.FromSeconds(30);
            var breakDuration = section.GetValue<TimeSpan?>("CircuitBreaker:BreakDuration") ?? TimeSpan.FromSeconds(15);
            var attemptTimeout = section.GetValue<TimeSpan?>("AttemptTimeout") ?? TimeSpan.FromSeconds(10);
            var concurrencyLimit = section.GetValue<int?>("ConcurrencyLimit") ?? 2;
            var concurrencyQueueLimit = section.GetValue<int?>("ConcurrencyQueueLimit") ?? 8;

            pipeline
                .AddConcurrencyLimiter(concurrencyLimit, concurrencyQueueLimit)
                .AddTimeout(new HttpTimeoutStrategyOptions { Timeout = totalTimeout })
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = retryMaxAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = retryBaseDelay,
                    ShouldHandle = static args =>
                        ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome))
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = failureRatio,
                    MinimumThroughput = minimumThroughput,
                    SamplingDuration = samplingDuration,
                    BreakDuration = breakDuration,
                    ShouldHandle = static args =>
                        ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome))
                })
                .AddTimeout(new HttpTimeoutStrategyOptions { Timeout = attemptTimeout });
        });
    }
}
