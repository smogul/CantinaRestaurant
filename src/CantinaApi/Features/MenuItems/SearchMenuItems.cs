using System.ComponentModel.DataAnnotations;
using CantinaApi.Common;
using CantinaApi.Data;
using CantinaApi.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

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
        CantinaDbContext db,
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

        var pattern = $"%{EscapeLikePattern(q)}%";
        var query = db.MenuItems
            .AsNoTracking()
            .Where(m => EF.Functions.ILike(m.Name, pattern, EscapeCharacter)
                || EF.Functions.ILike(m.Description, pattern, EscapeCharacter));

        if (type is not null)
        {
            query = query.Where(m => m.Type == type);
        }

        var results = await query
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Id)
            .Select(MenuItemMappings.ToResponse)
            .ToPagedResponseAsync(page, cancellationToken);

        return TypedResults.Ok(results);
    }

    // Wildcards typed by the user must match literally, so they are escaped before wrapping in %.
    private static string EscapeLikePattern(string value) => value
        .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter)
        .Replace("%", EscapeCharacter + "%")
        .Replace("_", EscapeCharacter + "_");
}
