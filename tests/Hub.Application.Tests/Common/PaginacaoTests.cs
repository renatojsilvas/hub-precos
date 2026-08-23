using Hub.Application.Common;

namespace Hub.Application.Tests.Common;

public sealed class PaginacaoTests
{
    [Fact]
    public void Criar_ComSkipNegativo_ClampaParaZero()
    {
        var paginacao = Paginacao.Criar(-1, 10);

        Assert.Equal(0, paginacao.Skip);
    }

    [Fact]
    public void Criar_ComTakeAcimaDoTeto_ClampaParaMaxPageSize()
    {
        var paginacao = Paginacao.Criar(0, 99999);

        Assert.Equal(PaginationDefaults.MaxPageSize, paginacao.Take);
    }

    [Fact]
    public void Criar_ComTakeAbaixoDoMinimo_ClampaParaMinPageSize()
    {
        var paginacao = Paginacao.Criar(0, 0);

        Assert.Equal(PaginationDefaults.MinPageSize, paginacao.Take);
    }

    [Fact]
    public void Criar_ComSkipETakeDentroDoTeto_PreservaOsValores()
    {
        var paginacao = Paginacao.Criar(20, 50);

        Assert.Equal(20, paginacao.Skip);
        Assert.Equal(50, paginacao.Take);
    }
}
