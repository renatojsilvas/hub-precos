using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Hub.Infrastructure.Tests.Ingestao;

public sealed class DependencyInjectionSchedulingTests : IClassFixture<DependencyInjectionSchedulingTests.SchedulingFixture>
{
    private const string CronPadrao = "0 0/15 * * * ?";

    private readonly SchedulingFixture _fixture;

    public DependencyInjectionSchedulingTests(SchedulingFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddInfrastructure_RegistraOJobTdIngestaoComOTriggerEOCronDaConfiguracao()
    {
        var jobDetail = await _fixture.AgendamentoAtivoScheduler.GetJobDetail(new JobKey("td-ingestao"));
        var trigger = await _fixture.AgendamentoAtivoScheduler.GetTrigger(new TriggerKey("td-ingestao-trigger"));

        Assert.NotNull(jobDetail);
        var cronTrigger = Assert.IsAssignableFrom<ICronTrigger>(trigger);
        Assert.Equal(CronPadrao, cronTrigger.CronExpressionString);
    }

    [Fact]
    public async Task AddInfrastructure_TriggerTdIngestao_TemPoliticaDeMisfireDoNothing()
    {
        var trigger = await _fixture.AgendamentoAtivoScheduler.GetTrigger(new TriggerKey("td-ingestao-trigger"));

        var cronTrigger = Assert.IsAssignableFrom<ICronTrigger>(trigger);
        Assert.Equal(MisfireInstruction.CronTrigger.DoNothing, cronTrigger.MisfireInstruction);
    }

    [Fact]
    public async Task AddInfrastructure_QuandoAgendamentoDesativado_NaoRegistraJobNemTrigger()
    {
        var jobDetail = await _fixture.AgendamentoInativoScheduler.GetJobDetail(new JobKey("td-ingestao"));
        var trigger = await _fixture.AgendamentoInativoScheduler.GetTrigger(new TriggerKey("td-ingestao-trigger"));

        Assert.Null(jobDetail);
        Assert.Null(trigger);
    }

    public sealed class SchedulingFixture : IAsyncLifetime
    {
        private ServiceProvider _providerAgendamentoAtivo = null!;
        private ServiceProvider _providerAgendamentoInativo = null!;

        public IScheduler AgendamentoAtivoScheduler { get; private set; } = null!;

        public IScheduler AgendamentoInativoScheduler { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            _providerAgendamentoAtivo = BuildProvider(BuildConfiguration());
            AgendamentoAtivoScheduler = await _providerAgendamentoAtivo
                .GetRequiredService<ISchedulerFactory>()
                .GetScheduler();

            _providerAgendamentoInativo = BuildProvider(BuildConfiguration(agendamentoAtivo: false));
            AgendamentoInativoScheduler = await _providerAgendamentoInativo
                .GetRequiredService<ISchedulerFactory>()
                .GetScheduler();
        }

        public async Task DisposeAsync()
        {
            await _providerAgendamentoAtivo.DisposeAsync();
            await _providerAgendamentoInativo.DisposeAsync();
        }

        private static IConfiguration BuildConfiguration(bool? agendamentoAtivo = null)
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Port=5432;Database=hub_precos_teste;Username=hub_app;Password=segredo",
                ["TdApi:CronSchedule"] = CronPadrao,
            };

            if (agendamentoAtivo is not null)
            {
                values["TdApi:AgendamentoAtivo"] = agendamentoAtivo.Value.ToString();
            }

            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        private static ServiceProvider BuildProvider(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(configuration);

            return services.BuildServiceProvider();
        }
    }
}
