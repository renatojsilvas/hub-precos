using System.Reflection;

namespace Hub.Architecture.Tests;

public sealed class DependencyTests
{
    private static readonly Assembly DomainAssembly = typeof(Hub.Domain.Common.Entity<>).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Hub.Application.DependencyInjection).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Hub.Infrastructure.Persistence.AppDbContext).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_ShouldNotReference_Application()
    {
        var referencedAssemblies = DomainAssembly.GetReferencedAssemblies();

        Assert.True(
            referencedAssemblies.All(a => a.Name != ApplicationAssembly.GetName().Name),
            "Domain must not depend on Application");
    }

    [Fact]
    public void Domain_ShouldNotReference_Infrastructure()
    {
        var referencedAssemblies = DomainAssembly.GetReferencedAssemblies();

        Assert.True(
            referencedAssemblies.All(a => a.Name != InfrastructureAssembly.GetName().Name),
            "Domain must not depend on Infrastructure");
    }

    [Fact]
    public void Domain_ShouldNotReference_Api()
    {
        var referencedAssemblies = DomainAssembly.GetReferencedAssemblies();

        Assert.True(
            referencedAssemblies.All(a => a.Name != ApiAssembly.GetName().Name),
            "Domain must not depend on API");
    }

    [Fact]
    public void Application_ShouldNotReference_Infrastructure()
    {
        var referencedAssemblies = ApplicationAssembly.GetReferencedAssemblies();

        Assert.True(
            referencedAssemblies.All(a => a.Name != InfrastructureAssembly.GetName().Name),
            "Application must not depend on Infrastructure");
    }

    [Fact]
    public void Application_ShouldNotReference_Api()
    {
        var referencedAssemblies = ApplicationAssembly.GetReferencedAssemblies();

        Assert.True(
            referencedAssemblies.All(a => a.Name != ApiAssembly.GetName().Name),
            "Application must not depend on API");
    }

    [Fact]
    public void Infrastructure_ShouldNotReference_Api()
    {
        var referencedAssemblies = InfrastructureAssembly.GetReferencedAssemblies();

        Assert.True(
            referencedAssemblies.All(a => a.Name != ApiAssembly.GetName().Name),
            "Infrastructure must not depend on API");
    }

    [Fact]
    public void GetReferencedAssemblies_DeveEnxergarReferenciasReais()
    {
        var applicationRefs = ApplicationAssembly.GetReferencedAssemblies();
        var infrastructureRefs = InfrastructureAssembly.GetReferencedAssemblies();

        Assert.True(
            applicationRefs.Any(a => a.Name == DomainAssembly.GetName().Name),
            "Application referencia Domain de verdade (ProjectReference no .csproj). Se esta " +
            "asserção falhar, o mecanismo GetReferencedAssemblies() parou de enxergar " +
            "referências e TODOS os testes negativos deste arquivo viraram vacuidade.");

        Assert.True(
            infrastructureRefs.Any(a => a.Name == ApplicationAssembly.GetName().Name),
            "Infrastructure referencia Application de verdade (ProjectReference no .csproj). " +
            "Mesmo raciocínio da asserção anterior.");
    }
}
