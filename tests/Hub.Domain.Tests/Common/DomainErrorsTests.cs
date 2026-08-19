using Hub.Domain.Common;

namespace Hub.Domain.Tests.Common;

public sealed class DomainErrorsTests
{
    [Fact]
    public void NotFound_ShouldReturnErrorWithEntityName()
    {
        var error = DomainErrors.General.NotFound("Titulo");

        Assert.Equal("General.NotFound", error.Code);
        Assert.Contains("Titulo", error.Description);
    }

    [Fact]
    public void Validation_ShouldReturnErrorWithMessage()
    {
        var error = DomainErrors.General.Validation("Field is required");

        Assert.Equal("General.Validation", error.Code);
        Assert.Equal("Field is required", error.Description);
    }

    [Fact]
    public void NullOrEmpty_ShouldReturnErrorWithFieldName()
    {
        var error = DomainErrors.General.NullOrEmpty("Name");

        Assert.Equal("General.NullOrEmpty", error.Code);
        Assert.Contains("Name", error.Description);
    }
}
