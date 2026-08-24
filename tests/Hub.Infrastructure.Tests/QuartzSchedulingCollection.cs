namespace Hub.Infrastructure.Tests;

[CollectionDefinition(Name)]
public sealed class QuartzSchedulingCollection : ICollectionFixture<QuartzSchedulingFixture>
{
    public const string Name = "Quartz DI scheduling";
}
