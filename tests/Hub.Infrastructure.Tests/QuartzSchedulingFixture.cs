using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Hub.Infrastructure.Tests;

public sealed class QuartzSchedulingFixture : IAsyncLifetime
{
    public const string CronPadrao = "0 0/15 * * * ?";
    public const int IntervaloSegundosPadrao = 5;

    private readonly List<ServiceProvider> _providers = [];

    public IScheduler TdAgendamentoAtivoScheduler { get; private set; } = null!;

    public IScheduler TdAgendamentoInativoScheduler { get; private set; } = null!;

    public IScheduler RelayAtivoScheduler { get; private set; } = null!;

    public IScheduler RelayInativoScheduler { get; private set; } = null!;

    public IScheduler TdInativoRelayAtivoScheduler { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        TdAgendamentoAtivoScheduler = await BuildSchedulerAsync(BuildConfiguration());
        TdAgendamentoInativoScheduler = await BuildSchedulerAsync(BuildConfiguration(tdAgendamentoAtivo: false));
        RelayAtivoScheduler = await BuildSchedulerAsync(BuildConfiguration());
        RelayInativoScheduler = await BuildSchedulerAsync(BuildConfiguration(relayAgendamentoAtivo: false));
        TdInativoRelayAtivoScheduler = await BuildSchedulerAsync(
            BuildConfiguration(tdAgendamentoAtivo: false, relayAgendamentoAtivo: true));
    }

    public async Task DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }

    private async Task<IScheduler> BuildSchedulerAsync(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        return await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    private static IConfiguration BuildConfiguration(bool? tdAgendamentoAtivo = null, bool? relayAgendamentoAtivo = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Host=localhost;Port=5432;Database=hub_precos_teste;Username=hub_app;Password=segredo",
            ["TdApi:CronSchedule"] = CronPadrao,
            ["Outbox:Relay:IntervaloSegundos"] = IntervaloSegundosPadrao.ToString(),
        };

        if (tdAgendamentoAtivo is not null)
        {
            values["TdApi:AgendamentoAtivo"] = tdAgendamentoAtivo.Value.ToString();
        }

        if (relayAgendamentoAtivo is not null)
        {
            values["Outbox:Relay:AgendamentoAtivo"] = relayAgendamentoAtivo.Value.ToString();
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
