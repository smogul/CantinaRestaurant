using CantinaApi.Common.Auth;
using CantinaApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CantinaApi.Data;

public static class DbSeeder
{
    private static readonly (string Name, string Description, decimal Price)[] Dishes =
    [
        ("Bantha Burger", "Smoked bantha patty with melted cheese and moisture-farm greens on a toasted bun.", 14.50m),
        ("Ronto Wrap", "Grilled ronto meat and spicy slaw in warm flatbread, a Mos Eisley street classic.", 9.75m),
        ("Nerf Steak", "Tender nerf sirloin with charred root vegetables and Corellian pepper sauce.", 24.00m),
        ("Dewback Ribs", "Slow-roasted dewback ribs glazed with Tatooine desert honey.", 19.50m),
        ("Twin Suns Tacos", "Two crispy tacos with fire-grilled womp rat and sun-dried salsa.", 11.25m),
        ("Mynock Wings", "Crispy mynock wings tossed in tangy Kessel hot sauce.", 12.00m),
        ("Tusken Trail Stew", "Hearty stew of spiced meat and desert tubers, slow-cooked for the long trek.", 13.50m),
        ("Endor Forest Salad", "Crisp greens, forest berries and toasted nuts from the forest moon of Endor.", 10.25m),
        ("Hutt Feast Platter", "A sharing platter of grilled meats, flatbreads and dips fit for a crime lord.", 39.99m),
        ("Gorg Skewers", "Street-food skewers of marinated gorg with a sticky chili glaze.", 8.50m),
    ];

    private static readonly (string Name, string Description, decimal Price)[] Drinks =
    [
        ("Blue Milk", "Chilled bantha milk with a hint of vanilla, straight from the Lars homestead.", 4.50m),
        ("Green Milk", "Creamy thala-siren milk from Ahch-To, lightly sweetened.", 5.00m),
        ("Jawa Juice", "A fizzy, fruity mocktail with a secret Jawa twist.", 6.25m),
        ("Tatooine Sunset", "Layered orange and pomegranate cooler inspired by the twin suns.", 7.50m),
        ("Corellian Ale", "Crisp amber ale brewed with Corellian grain.", 8.00m),
        ("Spotchka", "Glowing krill-based liquor, a favourite among smugglers.", 9.50m),
        ("Fuzzy Tauntaun", "Peach schnapps topped with frothy cream, warm enough for Hoth.", 10.00m),
        ("Bespin Fizz", "Sparkling Cloud City soda with citrus and mint.", 5.75m),
        ("Dagobah Swamp Tea", "Earthy herbal tea steeped Yoda-style and served hot.", 3.95m),
        ("Kessel Run Espresso", "A double shot of dark espresso, ready in under twelve parsecs.", 4.25m),
    ];

    private static readonly (string MenuItemName, int Stars, string Comment)[] CustomerRatings =
    [
        ("Blue Milk", 5, "Just like the Lars homestead used to make."),
        ("Bantha Burger", 4, "Huge portion, a little heavy on the smoke."),
        ("Jawa Juice", 3, "Fizzy and fun, but very sweet."),
    ];

    // Runs after migrations on every startup, so each step only seeds an empty table, soft-deleted rows included.
    public static async Task SeedAsync(
        CantinaDbContext db, SeedOptions options, PasswordHasher passwordHasher, TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await SeedMenuAsync(db, now, cancellationToken);
        await SeedUsersAndRatingsAsync(db, options, passwordHasher, now, cancellationToken);
    }

    private static async Task SeedMenuAsync(CantinaDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        if (await db.MenuItems.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            return;
        }

        db.MenuItems.AddRange(
            Dishes.Select(item => Create(item, MenuItemType.Dish, now))
                .Concat(Drinks.Select(item => Create(item, MenuItemType.Drink, now))));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedUsersAndRatingsAsync(
        CantinaDbContext db, SeedOptions options, PasswordHasher passwordHasher, DateTime now, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        if (!options.HasCredentials)
        {
            throw new InvalidOperationException(
                "Seeding needs Seed:AdminEmail, Seed:AdminPassword, Seed:CustomerEmail and Seed:CustomerPassword.");
        }

        var admin = CreateUser(options.AdminName, options.AdminEmail!, options.AdminPassword!, UserRole.Admin, passwordHasher, now);
        var customer = CreateUser(options.CustomerName, options.CustomerEmail!, options.CustomerPassword!, UserRole.Customer, passwordHasher, now);
        db.Users.AddRange(admin, customer);

        // Ratings belong to the customer only, because staff never rate their own menu.
        var names = CustomerRatings.Select(rating => rating.MenuItemName).ToArray();
        var menuItems = await db.MenuItems
            .Where(m => names.Contains(m.Name))
            .ToDictionaryAsync(m => m.Name, m => m.Id, cancellationToken);

        db.Ratings.AddRange(CustomerRatings
            .Where(rating => menuItems.ContainsKey(rating.MenuItemName))
            .Select(rating => new Rating
            {
                Id = Guid.CreateVersion7(),
                MenuItemId = menuItems[rating.MenuItemName],
                UserId = customer.Id,
                Stars = rating.Stars,
                Comment = rating.Comment,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static User CreateUser(
        string name, string email, string password, UserRole role, PasswordHasher passwordHasher, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name,
        Email = User.NormalizeEmail(email),
        PasswordHash = passwordHasher.Hash(password),
        Role = role,
        CreatedAtUtc = now,
    };

    private static MenuItem Create((string Name, string Description, decimal Price) item, MenuItemType type, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = item.Name,
        Description = item.Description,
        Price = item.Price,
        ImageUrl = $"https://placehold.co/600x400?text={Uri.EscapeDataString(item.Name)}",
        Type = type,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
    };
}
