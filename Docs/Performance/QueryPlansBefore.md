# Query plans before

Captured with EXPLAIN (ANALYZE, BUFFERS) against the load-test database (Scripts/LoadTestSeed.sql on top of the normal seed: 5,020 menu items, 202 users, 10,003 ratings) after VACUUM ANALYZE, with the API limited by docker-compose.loadtest.yml. The SQL is exactly what EF Core logged for each endpoint, with the parameter values written in; Npgsql sends these as unnamed statements, which Postgres plans with the actual values, so the plans match what the API runs. Each query ran once to warm the cache before the recorded run. The view and ratings queries use the item the load test picks (Bantha Burger, the first item by name with a rating). The two supplementary type=Drink plans are not endpoints from the brief; they were captured to judge whether a (Type, Name) index is needed.

## List page 1: count

A sequential scan reads all 5,020 rows (126 pages) just to count them. An exact count has to visit every live row, so no index removes this work, but it costs under 1 ms.

```sql
SELECT count(*)::int FROM "MenuItems" AS m WHERE NOT (m."IsDeleted")
```

```text
Aggregate  (cost=188.75..188.76 rows=1 width=4) (actual time=0.809..0.809 rows=1 loops=1)
  Buffers: shared hit=126
  ->  Seq Scan on "MenuItems" m  (cost=0.00..176.20 rows=5020 width=0) (actual time=0.005..0.566 rows=5020 loops=1)
        Filter: (NOT "IsDeleted")
        Buffers: shared hit=126
Planning:
  Buffers: shared hit=92
Planning Time: 0.419 ms
Execution Time: 0.880 ms
```

## List page 1: page (LIMIT 20 OFFSET 0)

Scans every row and keeps the first 20 by name with a top-N heap sort. Only 20 rows are returned, yet the cost grows with the whole table. An index on (Name, Id) for live items would let Postgres read the first 20 rows in order and stop.

```sql
SELECT m."Id", m."Name", m."Description", m."Price", m."ImageUrl", m."Type", m."CreatedAtUtc", m."UpdatedAtUtc" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") ORDER BY m."Name", m."Id" LIMIT 20 OFFSET 0
```

```text
Limit  (cost=309.78..309.83 rows=20 width=166) (actual time=1.490..1.492 rows=20 loops=1)
  Buffers: shared hit=132
  ->  Sort  (cost=309.78..322.33 rows=5020 width=166) (actual time=1.489..1.490 rows=20 loops=1)
        Sort Key: "Name", "Id"
        Sort Method: top-N heapsort  Memory: 33kB
        Buffers: shared hit=132
        ->  Seq Scan on "MenuItems" m  (cost=0.00..176.20 rows=5020 width=166) (actual time=0.004..0.594 rows=5020 loops=1)
              Filter: (NOT "IsDeleted")
              Buffers: shared hit=126
Planning:
  Buffers: shared hit=129
Planning Time: 0.433 ms
Execution Time: 1.578 ms
```

## List page 200: page (LIMIT 20 OFFSET 3980)

The slowest list query. It scans every row, then sorts 4,000 of them in memory (1.5 MB) to reach offset 3,980 and throws most of that work away. An ordered index removes the sort; the offset still has to step over 3,980 entries, which only keyset pagination would avoid.

```sql
SELECT m."Id", m."Name", m."Description", m."Price", m."ImageUrl", m."Type", m."CreatedAtUtc", m."UpdatedAtUtc" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") ORDER BY m."Name", m."Id" LIMIT 20 OFFSET 3980
```

```text
Limit  (cost=494.72..494.77 rows=20 width=166) (actual time=6.238..6.240 rows=20 loops=1)
  Buffers: shared hit=132
  ->  Sort  (cost=484.77..497.32 rows=5020 width=166) (actual time=5.967..6.133 rows=4000 loops=1)
        Sort Key: "Name", "Id"
        Sort Method: quicksort  Memory: 1487kB
        Buffers: shared hit=132
        ->  Seq Scan on "MenuItems" m  (cost=0.00..176.20 rows=5020 width=166) (actual time=0.004..0.660 rows=5020 loops=1)
              Filter: (NOT "IsDeleted")
              Buffers: shared hit=126
Planning:
  Buffers: shared hit=129
Planning Time: 0.437 ms
Execution Time: 6.320 ms
```

## Supplementary, list type=Drink page 1: count

Captured to judge the (Type, Name) candidate. The count already uses the existing unique (Type, lower(Name)) index as an index-only scan, so it needs nothing new.

```sql
SELECT count(*)::int FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND m."Type" = 'Drink'
```

```text
Aggregate  (cost=150.48..150.50 rows=1 width=4) (actual time=0.435..0.435 rows=1 loops=1)
  Buffers: shared hit=26
  ->  Index Only Scan using "IX_MenuItems_Type_LowerName" on "MenuItems" m  (cost=0.28..144.21 rows=2510 width=0) (actual time=0.032..0.285 rows=2510 loops=1)
        Index Cond: ("Type" = 'Drink'::text)
        Heap Fetches: 0
        Buffers: shared hit=26
Planning:
  Buffers: shared hit=100
Planning Time: 0.466 ms
Execution Time: 0.474 ms
```

## Supplementary, list type=Drink page 1: page (LIMIT 20 OFFSET 0)

Captured to judge the (Type, Name) candidate. It scans every row and sorts the 2,510 drinks for the top 20. With only two types at about half the menu each, a Name-ordered index can find 20 drinks after roughly 40 entries, so a separate (Type, Name) index is only worth adding if the After plan shows otherwise.

```sql
SELECT m."Id", m."Name", m."Description", m."Price", m."ImageUrl", m."Type", m."CreatedAtUtc", m."UpdatedAtUtc" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND m."Type" = 'Drink' ORDER BY m."Name", m."Id" LIMIT 20 OFFSET 0
```

```text
Limit  (cost=255.54..255.59 rows=20 width=166) (actual time=1.098..1.100 rows=20 loops=1)
  Buffers: shared hit=132
  ->  Sort  (cost=255.54..261.82 rows=2510 width=166) (actual time=1.097..1.098 rows=20 loops=1)
        Sort Key: "Name", "Id"
        Sort Method: top-N heapsort  Memory: 32kB
        Buffers: shared hit=132
        ->  Seq Scan on "MenuItems" m  (cost=0.00..188.75 rows=2510 width=166) (actual time=0.005..0.634 rows=2510 loops=1)
              Filter: ((NOT "IsDeleted") AND (("Type")::text = 'Drink'::text))
              Rows Removed by Filter: 2510
              Buffers: shared hit=126
Planning:
  Buffers: shared hit=128
Planning Time: 0.431 ms
Execution Time: 1.121 ms
```

## Search q=milk: count

A sequential scan runs the ILIKE contains match on Name and Description for every row; 4.9 of the 5.0 ms is spent in that filter. Trigram GIN indexes let Postgres find candidate rows from the index instead of testing every row.

```sql
SELECT count(*)::int FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND (m."Name" ILIKE '%milk%' ESCAPE '\' OR m."Description" ILIKE '%milk%' ESCAPE '\')
```

```text
Aggregate  (cost=204.45..204.46 rows=1 width=4) (actual time=4.923..4.923 rows=1 loops=1)
  Buffers: shared hit=126
  ->  Seq Scan on "MenuItems" m  (cost=0.00..201.30 rows=1259 width=0) (actual time=0.010..4.867 rows=1275 loops=1)
        Filter: ((NOT "IsDeleted") AND ((("Name")::text ~~* '%milk%'::text) OR (("Description")::text ~~* '%milk%'::text)))
        Rows Removed by Filter: 3745
        Buffers: shared hit=126
Planning:
  Buffers: shared hit=97
Planning Time: 0.501 ms
Execution Time: 4.971 ms
```

## Search q=milk: page (LIMIT 20 OFFSET 0)

The same full scan with ILIKE on every row, followed by a top-N sort. Almost all of the 5.2 ms is the pattern match, so this query and its count dominate the search endpoint.

```sql
SELECT m."Id", m."Name", m."Description", m."Price", m."ImageUrl", m."Type", m."CreatedAtUtc", m."UpdatedAtUtc" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND (m."Name" ILIKE '%milk%' ESCAPE '\' OR m."Description" ILIKE '%milk%' ESCAPE '\') ORDER BY m."Name", m."Id" LIMIT 20 OFFSET 0
```

```text
Limit  (cost=234.80..234.85 rows=20 width=166) (actual time=5.056..5.059 rows=20 loops=1)
  Buffers: shared hit=132
  ->  Sort  (cost=234.80..237.95 rows=1259 width=166) (actual time=5.055..5.056 rows=20 loops=1)
        Sort Key: "Name", "Id"
        Sort Method: top-N heapsort  Memory: 31kB
        Buffers: shared hit=132
        ->  Seq Scan on "MenuItems" m  (cost=0.00..201.30 rows=1259 width=166) (actual time=0.012..4.797 rows=1275 loops=1)
              Filter: ((NOT "IsDeleted") AND ((("Name")::text ~~* '%milk%'::text) OR (("Description")::text ~~* '%milk%'::text)))
              Rows Removed by Filter: 3745
              Buffers: shared hit=126
Planning:
  Buffers: shared hit=128
Planning Time: 1.242 ms
Execution Time: 5.096 ms
```

## View item by id

A primary key lookup followed by two correlated subqueries, one for the average and one for the count. Each rejoins MenuItems because of the soft-delete query filter and probes Ratings. Execution is 0.26 ms, but planning this three-table shape takes 1.1 ms, and the aggregation runs on every request. Storing the rating stats on the item removes both subqueries.

```sql
SELECT m."Id", m."Name", m."Description", m."Price", m."ImageUrl", m."Type", (SELECT avg(r."Stars"::double precision) FROM "Ratings" AS r INNER JOIN (SELECT m0."Id", m0."IsDeleted" FROM "MenuItems" AS m0 WHERE NOT (m0."IsDeleted")) AS m1 ON r."MenuItemId" = m1."Id" WHERE NOT (m1."IsDeleted") AND m."Id" = r."MenuItemId"), (SELECT count(*)::int FROM "Ratings" AS r0 INNER JOIN (SELECT m2."Id", m2."IsDeleted" FROM "MenuItems" AS m2 WHERE NOT (m2."IsDeleted")) AS m3 ON r0."MenuItemId" = m3."Id" WHERE NOT (m3."IsDeleted") AND m."Id" = r0."MenuItemId"), m."CreatedAtUtc", m."UpdatedAtUtc" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND m."Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f' LIMIT 1
```

```text
Limit  (cost=0.28..40.93 rows=1 width=178) (actual time=0.077..0.078 rows=1 loops=1)
  Buffers: shared hit=15
  ->  Index Scan using "PK_MenuItems" on "MenuItems" m  (cost=0.28..40.93 rows=1 width=178) (actual time=0.076..0.077 rows=1 loops=1)
        Index Cond: ("Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
        Filter: (NOT "IsDeleted")
        Buffers: shared hit=15
        SubPlan 1
          ->  Aggregate  (cost=19.96..19.97 rows=1 width=8) (actual time=0.025..0.026 rows=1 loops=1)
                Buffers: shared hit=6
                ->  Nested Loop  (cost=4.58..19.95 rows=2 width=4) (actual time=0.022..0.023 rows=1 loops=1)
                      Buffers: shared hit=6
                      ->  Index Scan using "PK_MenuItems" on "MenuItems" m0  (cost=0.28..8.30 rows=1 width=16) (actual time=0.005..0.005 rows=1 loops=1)
                            Index Cond: ("Id" = m."Id")
                            Filter: ((NOT "IsDeleted") AND (NOT "IsDeleted"))
                            Buffers: shared hit=3
                      ->  Bitmap Heap Scan on "Ratings" r  (cost=4.30..11.63 rows=2 width=20) (actual time=0.015..0.015 rows=1 loops=1)
                            Recheck Cond: ("MenuItemId" = m."Id")
                            Heap Blocks: exact=1
                            Buffers: shared hit=3
                            ->  Bitmap Index Scan on "IX_Ratings_MenuItemId_UserId"  (cost=0.00..4.30 rows=2 width=0) (actual time=0.010..0.010 rows=1 loops=1)
                                  Index Cond: ("MenuItemId" = m."Id")
                                  Buffers: shared hit=2
        SubPlan 2
          ->  Aggregate  (cost=12.64..12.66 rows=1 width=4) (actual time=0.017..0.017 rows=1 loops=1)
                Buffers: shared hit=6
                ->  Nested Loop  (cost=0.57..12.64 rows=2 width=0) (actual time=0.016..0.017 rows=1 loops=1)
                      Buffers: shared hit=6
                      ->  Index Scan using "PK_MenuItems" on "MenuItems" m2  (cost=0.28..8.30 rows=1 width=16) (actual time=0.004..0.004 rows=1 loops=1)
                            Index Cond: ("Id" = m."Id")
                            Filter: ((NOT "IsDeleted") AND (NOT "IsDeleted"))
                            Buffers: shared hit=3
                      ->  Index Only Scan using "IX_Ratings_MenuItemId_UserId" on "Ratings" r0  (cost=0.29..4.32 rows=2 width=16) (actual time=0.011..0.012 rows=1 loops=1)
                            Index Cond: ("MenuItemId" = m."Id")
                            Heap Fetches: 0
                            Buffers: shared hit=3
Planning:
  Buffers: shared hit=257
Planning Time: 0.959 ms
Execution Time: 0.207 ms
```

## Item ratings: existence check

A single primary key lookup; nothing to improve.

```sql
SELECT EXISTS (SELECT 1 FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND m."Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f')
```

```text
Result  (cost=8.30..8.31 rows=1 width=1) (actual time=0.013..0.013 rows=1 loops=1)
  Buffers: shared hit=3
  InitPlan 1 (returns $0)
    ->  Index Scan using "PK_MenuItems" on "MenuItems" m  (cost=0.28..8.30 rows=1 width=0) (actual time=0.011..0.011 rows=1 loops=1)
          Index Cond: ("Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
          Filter: (NOT "IsDeleted")
          Buffers: shared hit=3
Planning:
  Buffers: shared hit=100
Planning Time: 0.343 ms
Execution Time: 0.041 ms
```

## Item ratings: count

A primary key lookup plus an index-only scan on the unique (MenuItemId, UserId) index. It is already cheap.

```sql
SELECT count(*)::int FROM "Ratings" AS r INNER JOIN (SELECT m."Id", m."IsDeleted" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted")) AS m0 ON r."MenuItemId" = m0."Id" WHERE NOT (m0."IsDeleted") AND r."MenuItemId" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'
```

```text
Aggregate  (cost=12.64..12.66 rows=1 width=4) (actual time=0.047..0.048 rows=1 loops=1)
  Buffers: shared hit=6
  ->  Nested Loop  (cost=0.57..12.64 rows=2 width=0) (actual time=0.044..0.045 rows=1 loops=1)
        Buffers: shared hit=6
        ->  Index Scan using "PK_MenuItems" on "MenuItems" m  (cost=0.28..8.30 rows=1 width=16) (actual time=0.025..0.025 rows=1 loops=1)
              Index Cond: ("Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
              Filter: ((NOT "IsDeleted") AND (NOT "IsDeleted"))
              Buffers: shared hit=3
        ->  Index Only Scan using "IX_Ratings_MenuItemId_UserId" on "Ratings" r  (cost=0.29..4.32 rows=2 width=16) (actual time=0.018..0.018 rows=1 loops=1)
              Index Cond: ("MenuItemId" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
              Heap Fetches: 0
              Buffers: shared hit=3
Planning:
  Buffers: shared hit=227
Planning Time: 0.610 ms
Execution Time: 0.112 ms
```

## Item ratings: page (LIMIT 20 OFFSET 0)

Cheap. The planner fetches the item's ratings through the unique (MenuItemId, UserId) index, sorts the few rows by CreatedAtUtc descending, and hash joins the 202 users. It prefers that index over the existing (MenuItemId, CreatedAtUtc) index because an item has only a couple of ratings, so a descending (MenuItemId, CreatedAtUtc) index would not change this plan and is not added.

```sql
SELECT s."Id", s."MenuItemId", u."Name", s."Stars", s."Comment", s."CreatedAtUtc", s."UpdatedAtUtc" FROM (SELECT r."Id", r."Comment", r."CreatedAtUtc", r."MenuItemId", r."Stars", r."UpdatedAtUtc", r."UserId" FROM "Ratings" AS r INNER JOIN (SELECT m."Id", m."IsDeleted" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted")) AS m0 ON r."MenuItemId" = m0."Id" WHERE NOT (m0."IsDeleted") AND r."MenuItemId" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f' ORDER BY r."CreatedAtUtc" DESC, r."Id" DESC LIMIT 20 OFFSET 0) AS s INNER JOIN "Users" AS u ON s."UserId" = u."Id" ORDER BY s."CreatedAtUtc" DESC, s."Id" DESC
```

```text
Sort  (cost=27.81..27.82 rows=2 width=133) (actual time=0.114..0.116 rows=1 loops=1)
  Sort Key: s."CreatedAtUtc" DESC, s."Id" DESC
  Sort Method: quicksort  Memory: 25kB
  Buffers: shared hit=17
  ->  Hash Join  (cost=20.01..27.80 rows=2 width=133) (actual time=0.061..0.095 rows=1 loops=1)
        Hash Cond: (u."Id" = s."UserId")
        Buffers: shared hit=11
        ->  Seq Scan on "Users" u  (cost=0.00..7.02 rows=202 width=38) (actual time=0.002..0.020 rows=202 loops=1)
              Buffers: shared hit=5
        ->  Hash  (cost=19.98..19.98 rows=2 width=127) (actual time=0.046..0.047 rows=1 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=6
              ->  Subquery Scan on s  (cost=19.96..19.98 rows=2 width=127) (actual time=0.042..0.042 rows=1 loops=1)
                    Buffers: shared hit=6
                    ->  Limit  (cost=19.96..19.96 rows=2 width=127) (actual time=0.041..0.041 rows=1 loops=1)
                          Buffers: shared hit=6
                          ->  Sort  (cost=19.96..19.96 rows=2 width=127) (actual time=0.040..0.040 rows=1 loops=1)
                                Sort Key: r."CreatedAtUtc" DESC, r."Id" DESC
                                Sort Method: quicksort  Memory: 25kB
                                Buffers: shared hit=6
                                ->  Nested Loop  (cost=4.58..19.95 rows=2 width=127) (actual time=0.032..0.033 rows=1 loops=1)
                                      Buffers: shared hit=6
                                      ->  Index Scan using "PK_MenuItems" on "MenuItems" m  (cost=0.28..8.30 rows=1 width=16) (actual time=0.020..0.020 rows=1 loops=1)
                                            Index Cond: ("Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
                                            Filter: ((NOT "IsDeleted") AND (NOT "IsDeleted"))
                                            Buffers: shared hit=3
                                      ->  Bitmap Heap Scan on "Ratings" r  (cost=4.30..11.63 rows=2 width=127) (actual time=0.010..0.010 rows=1 loops=1)
                                            Recheck Cond: ("MenuItemId" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
                                            Heap Blocks: exact=1
                                            Buffers: shared hit=3
                                            ->  Bitmap Index Scan on "IX_Ratings_MenuItemId_UserId"  (cost=0.00..4.30 rows=2 width=0) (actual time=0.008..0.008 rows=1 loops=1)
                                                  Index Cond: ("MenuItemId" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
                                                  Buffers: shared hit=2
Planning:
  Buffers: shared hit=300
Planning Time: 1.003 ms
Execution Time: 0.193 ms
```
