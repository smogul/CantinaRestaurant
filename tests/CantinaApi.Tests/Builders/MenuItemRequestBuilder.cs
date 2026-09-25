using System.Text.Json;
using System.Text.Json.Nodes;
using CantinaApi.Data.Entities;
using CantinaApi.Features.MenuItems;
using CantinaApi.Tests.Infrastructure;

namespace CantinaApi.Tests.Builders;

public sealed class MenuItemRequestBuilder
{
    private static int _sequence;

    private string _name = $"Test Dish {Interlocked.Increment(ref _sequence)}";
    private string _description = "A hearty plate from the Outer Rim.";
    private decimal _price = 12.50m;
    private string _imageUrl = "https://images.example.com/menu/dish.png";
    private MenuItemType? _type = MenuItemType.Dish;

    public MenuItemRequestBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public MenuItemRequestBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public MenuItemRequestBuilder WithPrice(decimal price)
    {
        _price = price;
        return this;
    }

    public MenuItemRequestBuilder WithImageUrl(string imageUrl)
    {
        _imageUrl = imageUrl;
        return this;
    }

    public MenuItemRequestBuilder WithType(MenuItemType type)
    {
        _type = type;
        return this;
    }

    public MenuItemRequest Build() => new(_name, _description, _price, _imageUrl, _type);

    // A JSON form so tests can send values the typed request cannot hold, such as an unknown type.
    public JsonObject BuildJson() =>
        JsonSerializer.SerializeToNode(Build(), ApiClientExtensions.JsonOptions)!.AsObject();
}
