using System.ComponentModel.DataAnnotations;

namespace CantinaApi.Common;

public sealed record PageRequest(
    [property: Range(1, int.MaxValue)] int Page = 1,
    [property: Range(1, PageRequest.MaxPageSize)] int PageSize = PageRequest.DefaultPageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    // Clamped so a huge page number yields an empty page instead of an integer overflow.
    public int Skip => (int)Math.Min((long)(Page - 1) * PageSize, int.MaxValue);
}
