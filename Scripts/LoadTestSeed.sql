-- Bulk data for load tests: 5,000 menu items, 200 customers and 10,000 ratings.
-- Safe to run repeatedly; it does nothing once the marker user loadtest-user-1@cantina.example exists.
-- Schema follows the EF Core migrations: quoted PascalCase names, uuid ids, enums stored as their names.
DO $$
DECLARE
    -- Words mixed into descriptions so searches such as "milk" match a realistic slice of the menu.
    words text[] := ARRAY['milk', 'spice', 'smoked', 'cloud', 'bantha', 'desert', 'citrus', 'honey', 'pepper', 'berry', 'frost', 'ember'];
    word_count int := array_length(words, 1);
    -- Ids and timestamps come from the row index and this fixed instant instead of randomness or the clock, so every fresh seed is identical.
    anchor timestamptz := '2026-01-01 00:00:00+00';
BEGIN
    IF EXISTS (SELECT 1 FROM "Users" WHERE "Email" = 'loadtest-user-1@cantina.example') THEN
        RAISE NOTICE 'Load test data already present, nothing inserted.';
        RETURN;
    END IF;

    -- Item numbers 1 to 2,500 are dishes and 2,501 to 5,000 are drinks.
    CREATE TEMP TABLE load_test_items ON COMMIT DROP AS
    SELECT n, md5('load-test-item-' || n)::uuid AS id
    FROM generate_series(1, 5000) AS n;

    INSERT INTO "MenuItems" ("Id", "Name", "Description", "Price", "ImageUrl", "Type", "IsDeleted", "CreatedAtUtc", "UpdatedAtUtc")
    SELECT
        i.id,
        CASE WHEN i.n <= 2500 THEN 'Load Test Dish ' || i.n ELSE 'Load Test Drink ' || (i.n - 2500) END,
        -- Each word comes from a different digit of the item number, so combinations vary and the first two words never repeat.
        format('A %s and %s blend finished with a hint of %s.',
            words[1 + i.n % word_count],
            words[1 + (i.n % word_count + 1 + (i.n / word_count) % (word_count - 1)) % word_count],
            words[1 + (i.n / (word_count * (word_count - 1))) % word_count]),
        round(1 + ((i.n * 37) % 9900) / 100.0, 2),
        'https://placehold.co/600x400?text=Load+Test+' || i.n,
        CASE WHEN i.n <= 2500 THEN 'Dish' ELSE 'Drink' END,
        false,
        anchor - make_interval(mins => i.n),
        anchor - make_interval(mins => i.n)
    FROM load_test_items AS i;

    CREATE TEMP TABLE load_test_users ON COMMIT DROP AS
    SELECT n, md5('load-test-user-' || n)::uuid AS id
    FROM generate_series(1, 200) AS n;

    -- One valid work factor 12 hash of a random password that was discarded; logins fail cleanly instead of throwing.
    INSERT INTO "Users" ("Id", "Name", "Email", "PasswordHash", "Role", "FailedLoginAttempts", "LockoutEndUtc", "CreatedAtUtc")
    SELECT
        u.id,
        'Load Test Customer ' || u.n,
        'loadtest-user-' || u.n || '@cantina.example',
        '$2a$12$YFriFd.PUxZmSuqOT8AHw.G.4lxqr89UirIEPmEXVbRTuctGB.mZi',
        'Customer',
        0,
        NULL,
        anchor
    FROM load_test_users AS u;

    -- User n rates items (n-1)*50+1 to n*50, wrapping at 5,000, so each user's 50 items are distinct and every item gets 2 ratings.
    INSERT INTO "Ratings" ("Id", "MenuItemId", "UserId", "Stars", "Comment", "CreatedAtUtc", "UpdatedAtUtc")
    SELECT
        md5('load-test-rating-' || u.n || '-' || k)::uuid,
        i.id,
        u.id,
        1 + ((u.n * 3 + k) % 5),
        CASE WHEN k % 3 = 0 THEN format('Load test review %s of %s: the %s really comes through.', k + 1, u.n, words[1 + (k % word_count)]) END,
        anchor - make_interval(mins => u.n * 50 + k),
        anchor - make_interval(mins => u.n * 50 + k)
    FROM load_test_users AS u
    CROSS JOIN generate_series(0, 49) AS k
    JOIN load_test_items AS i ON i.n = ((u.n - 1) * 50 + k) % 5000 + 1;
END $$;

SELECT
    (SELECT count(*) FROM "MenuItems") AS menu_items,
    (SELECT count(*) FROM "Users") AS users,
    (SELECT count(*) FROM "Ratings") AS ratings;
