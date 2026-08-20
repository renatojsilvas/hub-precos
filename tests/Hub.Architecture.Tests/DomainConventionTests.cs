using System.Reflection;

namespace Hub.Architecture.Tests;

public sealed class DomainConventionTests
{
    private static readonly Assembly DomainAssembly = typeof(Hub.Domain.Common.Entity<>).Assembly;

    private static IEnumerable<Type> GetDomainClasses() =>
        DomainAssembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && !t.IsNested);

    [Fact]
    public void AllDomainClasses_ShouldBeSealed()
    {
        var domainClasses = GetDomainClasses().ToList();

        // Guarda contra vacuidade: sem tipos concretos para inspecionar, o teste
        // abaixo passaria sempre, mesmo sem checar nada. Hoje já existem Result e
        // Result<T> no assembly, então este guard não deveria disparar.
        Assert.True(
            domainClasses.Count > 0,
            "esta asserção só é uma convenção real se houver classes públicas no Domain para inspecionar");

        var nonSealedClasses = domainClasses
            .Where(t => !t.IsSealed)
            .Select(t => t.FullName)
            .ToList();

        Assert.True(
            nonSealedClasses.Count == 0,
            $"all Domain classes must be sealed, but found: {string.Join(", ", nonSealedClasses)}");
    }

    [Fact]
    public void DomainRecords_ShouldBeSealed()
    {
        var domainRecords = DomainAssembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && !t.IsNested && IsRecord(t))
            .ToList();

        // Guarda contra vacuidade: hoje o record Error já existe no Domain, então
        // este guard não deveria disparar.
        Assert.True(
            domainRecords.Count > 0,
            "esta asserção só é uma convenção real se houver records públicos no Domain para inspecionar");

        var nonSealedRecords = domainRecords
            .Where(t => !t.IsSealed)
            .Select(t => t.FullName)
            .ToList();

        Assert.True(
            nonSealedRecords.Count == 0,
            $"all Domain records must be sealed, but found: {string.Join(", ", nonSealedRecords)}");
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$") is not null;
}
