using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CantinaApi.Features.MenuItems;
using CantinaApi.Features.Ratings;

namespace CantinaApi.Tests.Infrastructure;

public static class ApiClientExtensions
{
    public const string MenuItemsRoute = "/api/menu-items";

    // Mirrors the API's camelCase JSON with enums as strings.
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string MenuItemRoute(Guid id) => $"{MenuItemsRoute}/{id}";

    public static string RatingsRoute(Guid id) => $"{MenuItemsRoute}/{id}/ratings";

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(url, body, JsonOptions, cancellationToken);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body, CancellationToken cancellationToken) =>
        client.PutAsJsonAsync(url, body, JsonOptions, cancellationToken);

    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The response body was empty.");

    public static async Task<MenuItemResponse> CreateMenuItemAsync(
        this HttpClient client, MenuItemRequest request, CancellationToken cancellationToken)
    {
        var response = await client.PostJsonAsync(MenuItemsRoute, request, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<MenuItemResponse>(cancellationToken);
    }

    public static async Task<RatingResponse> RateAsync(
        this HttpClient client, Guid menuItemId, CreateRatingRequest request, CancellationToken cancellationToken)
    {
        var response = await client.PostJsonAsync(RatingsRoute(menuItemId), request, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<RatingResponse>(cancellationToken);
    }

    // Asserts an RFC 7807 body and returns it for further checks.
    public static async Task<JsonElement> AssertProblemAsync(
        this HttpResponseMessage response, HttpStatusCode expectedStatus, CancellationToken cancellationToken)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.ReadJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        return problem;
    }

    public static IEnumerable<string> ErrorKeys(this JsonElement problem) =>
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name);
}
