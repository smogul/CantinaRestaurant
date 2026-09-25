namespace CantinaApi.Tests.Infrastructure;

// One app instance shared by every test class; classes in a collection run one at a time, so resets never race.
[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<CustomWebApplicationFactory>;
