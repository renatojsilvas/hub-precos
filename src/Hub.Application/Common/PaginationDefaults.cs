namespace Hub.Application.Common;

public static class PaginationDefaults
{
    public const int DefaultPageSize = 100;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 500;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize)
    {
        var normalizedPage = Math.Max(1, page ?? 1);
        var normalizedPageSize = Math.Clamp(pageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);

        return (normalizedPage, normalizedPageSize);
    }

    public static int ComputeSkip(int page, int pageSize, int total)
    {
        var skip = (long)(page - 1) * pageSize;

        return (int)Math.Clamp(skip, 0, total);
    }
}
