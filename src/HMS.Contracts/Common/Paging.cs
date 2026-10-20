namespace HMS.Contracts.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Query-string paging. Lists are always bounded.</summary>
public sealed class PageQuery
{
    public const int MaxPageSize = 200;
    private int _page = 1;
    private int _pageSize = 50;

    public int Page { get => _page; set => _page = Math.Max(1, value); }
    public int PageSize { get => _pageSize; set => _pageSize = Math.Clamp(value, 1, MaxPageSize); }
    public string? Search { get; set; }

    public int Skip => (Page - 1) * PageSize;
}
