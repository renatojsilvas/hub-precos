namespace Hub.Application.Common;

public sealed record Paginacao
{
    private Paginacao(int skip, int take)
    {
        Skip = skip;
        Take = take;
    }

    public int Skip { get; }
    public int Take { get; }

    public static Paginacao Criar(int skip, int take)
    {
        var skipClamped = Math.Max(0, skip);
        var takeClamped = Math.Clamp(take, PaginationDefaults.MinPageSize, PaginationDefaults.MaxPageSize);

        return new Paginacao(skipClamped, takeClamped);
    }
}
