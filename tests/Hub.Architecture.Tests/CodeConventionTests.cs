using System.Reflection;

namespace Hub.Architecture.Tests;

public sealed class CodeConventionTests
{
    private static readonly Assembly ApplicationAssembly = typeof(Hub.Application.DependencyInjection).Assembly;

    [Fact]
    public void ApplicationClasses_ShouldBeSealed()
    {
        var applicationClasses = ApplicationAssembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && !t.IsNested
                && !t.IsInterface
                && !IsStaticClass(t))
            .ToList();

        Assert.True(
            applicationClasses.Count > 0,
            "esta asserção só é uma convenção real se houver classes públicas no Application para inspecionar");

        var nonSealedClasses = applicationClasses
            .Where(t => !t.IsSealed)
            .Select(t => t.FullName)
            .ToList();

        Assert.True(
            nonSealedClasses.Count == 0,
            $"all Application classes must be sealed, but found: {string.Join(", ", nonSealedClasses)}");
    }

    private static bool IsStaticClass(Type type) =>
        type.IsAbstract && type.IsSealed;
}
