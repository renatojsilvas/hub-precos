using Quartz;

namespace Hub.Infrastructure.Tests.Ingestao;

internal sealed class FakeJobExecutionContext : IJobExecutionContext
{
    public FakeJobExecutionContext(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }

    public IScheduler Scheduler => throw new NotSupportedException();

    public ITrigger Trigger => throw new NotSupportedException();

    public ICalendar? Calendar => throw new NotSupportedException();

    public bool Recovering => false;

    public TriggerKey RecoveringTriggerKey => throw new NotSupportedException();

    public int RefireCount => 0;

    public JobDataMap MergedJobDataMap => [];

    public IJobDetail JobDetail => throw new NotSupportedException();

    public IJob JobInstance => throw new NotSupportedException();

    public DateTimeOffset FireTimeUtc => DateTimeOffset.UtcNow;

    public DateTimeOffset? ScheduledFireTimeUtc => null;

    public DateTimeOffset? PreviousFireTimeUtc => null;

    public DateTimeOffset? NextFireTimeUtc => null;

    public string FireInstanceId => "fake-fire-instance";

    public object? Result { get; set; }

    public TimeSpan JobRunTime => TimeSpan.Zero;

    public void Put(object key, object objectValue) => throw new NotSupportedException();

    public object? Get(object key) => throw new NotSupportedException();
}
