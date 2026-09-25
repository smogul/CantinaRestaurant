using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using CantinaApi.Common;
using CantinaApi.Data.Entities;

namespace CantinaApi.Features.MenuItems;

public sealed record MenuItemRequest(
    [property: Required, MaxLength(MenuItem.NameMaxLength)] string Name,
    [property: Required, MaxLength(MenuItem.DescriptionMaxLength)] string Description,
    [property: Range(typeof(decimal), "0.01", "100000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal Price,
    [property: Required, MaxLength(MenuItem.ImageUrlMaxLength), Url, HttpUrl] string ImageUrl,
    [property: Required] MenuItemType? Type);

[ImmutableObject(true)]
public sealed record MenuItemResponse(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    string ImageUrl,
    MenuItemType Type,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

[ImmutableObject(true)]
public sealed record MenuItemDetailsResponse(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    string ImageUrl,
    MenuItemType Type,
    double? AverageRating,
    int RatingCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public static class MenuItemMappings
{
    public static readonly Expression<Func<MenuItem, MenuItemResponse>> ToResponse = m =>
        new MenuItemResponse(m.Id, m.Name, m.Description, m.Price, m.ImageUrl, m.Type, m.CreatedAtUtc, m.UpdatedAtUtc);

    private static readonly Func<MenuItem, MenuItemResponse> ToResponseCompiled = ToResponse.Compile();

    public static MenuItemResponse ToResponseDto(this MenuItem item) => ToResponseCompiled(item);
}
