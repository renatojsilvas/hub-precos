using Hub.Infrastructure.Tests;
using Quartz;

namespace Hub.Infrastructure.Tests.Ingestao;

[Collection(QuartzSchedulingCollection.Name)]
public sealed class DependencyInjectionSchedulingTests
{
    private readonly QuartzSchedulingFixture _fixture;

    public DependencyInjectionSchedulingTests(QuartzSchedulingFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddInfrastructure_RegistraOJobTdIngestaoComOTriggerEOCronDaConfiguracao()
    {
        var jobDetail = await _fixture.TdAgendamentoAtivoScheduler.GetJobDetail(new JobKey("td-ingestao"));
        var trigger = await _fixture.TdAgendamentoAtivoScheduler.GetTrigger(new TriggerKey("td-ingestao-trigger"));

        Assert.NotNull(jobDetail);
        var cronTrigger = Assert.IsAssignableFrom<ICronTrigger>(trigger);
        Assert.Equal(QuartzSchedulingFixture.CronPadrao, cronTrigger.CronExpressionString);
    }

    [Fact]
    public async Task AddInfrastructure_TriggerTdIngestao_TemPoliticaDeMisfireDoNothing()
    {
        var trigger = await _fixture.TdAgendamentoAtivoScheduler.GetTrigger(new TriggerKey("td-ingestao-trigger"));

        var cronTrigger = Assert.IsAssignableFrom<ICronTrigger>(trigger);
        Assert.Equal(MisfireInstruction.CronTrigger.DoNothing, cronTrigger.MisfireInstruction);
    }

    [Fact]
    public async Task AddInfrastructure_QuandoAgendamentoDesativado_NaoRegistraJobNemTrigger()
    {
        var jobDetail = await _fixture.TdAgendamentoInativoScheduler.GetJobDetail(new JobKey("td-ingestao"));
        var trigger = await _fixture.TdAgendamentoInativoScheduler.GetTrigger(new TriggerKey("td-ingestao-trigger"));

        Assert.Null(jobDetail);
        Assert.Null(trigger);
    }
}
