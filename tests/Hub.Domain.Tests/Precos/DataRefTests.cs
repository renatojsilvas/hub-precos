using Hub.Domain.Precos;

namespace Hub.Domain.Tests.Precos;

public sealed class DataRefTests
{
    [Fact]
    public void Create_ComDataValida_DeveTerSucesso()
    {
        var data = new DateOnly(2029, 3, 1);

        var result = DataRef.Create(data);

        Assert.True(result.IsSuccess);
        Assert.Equal(data, result.Value.Value);
    }

    [Fact]
    public void Create_ComDataMinValue_DeveFalhar()
    {
        var result = DataRef.Create(DateOnly.MinValue);

        Assert.True(result.IsFailure);
        Assert.Equal(PrecoErrors.DataRefVazia, result.Error);
    }
}
