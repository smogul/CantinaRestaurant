using System.ComponentModel;

namespace CantinaApi.Common;

// Sealed and immutable, so HybridCache hands out the cached instance instead of deserialising a copy on every hit.
[ImmutableObject(true)]
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);
