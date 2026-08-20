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

    // CONTROLE POSITIVO — guarda contra vacuidade dos testes acima.
    //
    // Todos os testes deste arquivo são asserções NEGATIVAS ("X não referencia Y"), e
    // asserção negativa passa tanto quando a regra é respeitada quanto quando o mecanismo
    // de detecção parou de funcionar. Se `GetReferencedAssemblies()` um dia devolvesse
    // vazio — outro TargetFramework, trimming, mudança de SDK —, os seis testes acima
    // continuariam verdes sem checar nada, e a regra de camadas de PADROES.md §1 estaria
    // desprotegida em silêncio.
    //
    // Este teste afirma o oposto sobre dependências que EXISTEM de verdade
    // (Application -> Domain e Infrastructure -> Application, ambas declaradas nos
    // .csproj). Ele só passa se o mecanismo realmente enxerga referências reais.
    //
    // Nota sobre o que NÃO deu para fazer: a prova mais direta seria injetar uma violação
    // real (referência binária de Hub.Application dentro de Hub.Domain) e ver o teste
    // correspondente falhar. Tentado e descartado: o build quebra antes de chegar ao
    // teste, com MSB4018/GenerateDepsFile "An item with the same key has already been
    // added. Key: Hub.Domain", porque o deps file de Hub.Application já contém Hub.Domain.
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
