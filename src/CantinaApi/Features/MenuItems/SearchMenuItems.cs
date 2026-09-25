using System.ComponentModel.DataAnnotations;
using CantinaApi.Common;
using CantinaApi.Common.Caching;
using CantinaApi.Data;
using CantinaApi.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace CantinaApi.Features.MenuItems;

public static class SearchMenuItems
{
    private const int MaxQueryLength = 100;
    private const string EscapeCharacter = "\\";

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/search", HandleAsync)
            .WithName(nameof(SearchMenuItems))
            .WithSummary("Search menu item names and descriptions, case-insensitively.");

    private static async Task<Results<Ok<PagedResponse<MenuItemResponse>>, ValidationProblem>> HandleAsync(
        [Required, StringLength(MaxQueryLength, MinimumLength = 1)] string? q,
        [AsParameters] PageRequest page,
        [EnumDataType(typeof(MenuItemType))] MenuItemType? type,
        IDbContextFactory<CantinaDbContext> dbFactory,
        HybridCache cache,
        IOptions<CachingOptions> caching,
        CancellationToken cancellationToken)
    {
        // Built-in validation skips [Required] when q is absent from the query string, so check it here.
        if (string.IsNullOrWhiteSpace(q))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(q)] = ["The q field is required."],
            });
        }

        // The match ignores case, so trimming and lowercasing gives one cache entry for every spelling of the same search.
        var term = q.Trim();
        var results = await cache.GetOrLoadAsync(
            CacheKeys.MenuSearch(term.ToLowerInvariant(), type, page),
            async token => await LoadAsync(dbFactory, term, type, page, token),
            caching.Value.SearchEntry,
            [CacheKeys.MenuItemsTag],
            cancellationToken);

        return TypedResults.Ok(results);
    }

    // The factory builds its own context because its result may be shared with other requests waiting on the same key.
    private static async Task<PagedResponse<MenuItemResponse>> LoadAsync(
        IDbContextFactory<CantinaDbContext> dbFactory, string term, MenuItemType? type, PageRequest page, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var pattern = $"%{EscapeLikePattern(term)}%";
        var query = db.MenuItems
            .AsNoTracking()
            .Where(m => EF.Functions.ILike(m.Name, pattern, EscapeCharacter)
                || EF.Functions.ILike(m.Description, pattern, EscapeCharacter));

        if (type is not null)
        {
            query = query.Where(m => m.Type == type);
        }

        return await query
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Id)
            .Select(MenuItemMappings.ToResponse)
            .ToPagedResponseAsync(page, cancellationToken);
    }

    // Wildcards typed by the user must match literally, so they are escaped before wrapping in %.
    private static string EscapeLikePattern(string value) => value
        .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter)
        .Replace("%", EscapeCharacter + "%")
        .Replace("_", EscapeCharacter + "_");
}
