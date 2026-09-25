# Query plans after

Captured the same way as QueryPlansBefore.md, against the same load-test database after the AddSearchIndexesAndRatingStats migration and a fresh VACUUM ANALYZE, with the API still limited by docker-compose.loadtest.yml. List and search SQL is unchanged; the view query now reads the stored rating columns. These plans show the cost of a cache miss; with HybridCache in place, repeat requests within the expiry do not reach the database at all. New indexes: IX_MenuItems_Name_Id_Live (Name, Id) and trigram GIN indexes on Name and Description, all limited to live items. Candidates the plans did not justify, and which were not added: a separate (Type, Name) index and a descending (MenuItemId, CreatedAtUtc) rating index.

## List page 1: count

Now an index-only scan of the new partial (Name, Id) index instead of reading the table, but an exact count still visits all 5,020 live entries, so the time barely moves (0.95 to 0.88 ms). Caching, not indexing, is what saves this query on repeat requests.

```sql
SELECT count(*)::int FROM "MenuItems" AS m WHERE NOT (m."IsDeleted")
```

```text
Aggregate  (cost=284.13..284.15 rows=1 width=4) (actual time=0.789..0.790 rows=1 loops=1)
  Buffers: shared hit=49
  ->  Index Only Scan using "IX_MenuItems_Name_Id_Live" on "MenuItems" m  (cost=0.28..271.58 rows=5020 width=0) (actual time=0.031..0.480 rows=5020 loops=1)
        Heap Fetches: 0
        Buffers: shared hit=49
Planning:
  Buffers: shared hit=141
Planning Time: 0.620 ms
Execution Time: 0.880 ms
```

## List page 1: page (LIMIT 20 OFFSET 0)

Reads the first 20 entries of the (Name, Id) index in order and stops. The full scan and top-N sort are gone: 1.66 ms before, 0.08 ms now.

```sql
SELECT m."Id", m."Name", m."Description", m."Price", m."ImageUrl", m."Type", m."CreatedAtUtc", m."UpdatedAtUtc" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") ORDER BY m."Name", m."Id" LIMIT 20 OFFSET 0
```

```text
Limit  (cost=0.28..4.72 rows=20 width=166) (actual time=0.023..0.038 rows=20 loops=1)
  Buffers: shared hit=11
  ->  Index Scan using "IX_MenuItems_Name_Id_Live" on "MenuItems" m  (cost=0.28..1113.72 rows=5020 width=166) (actual time=0.022..0.036 rows=20 loops=1)
        Buffers: shared hit=11
Planning:
  Buffers: shared hit=176
Planning Time: 0.538 ms
Execution Time: 0.080 ms
```

## List page 200: page (LIMIT 20 OFFSET 3980)

Unchanged: the planner still picks a full scan plus an in-memory sort (6.4 ms). Walking the index to offset 3,980 touches about 889 buffers in index order instead of 126 in sequence, and Postgres's default random_page_cost of 4 assumes those reads are slow. Forcing the index path on this data ran in 1.1 ms, so lowering random_page_cost for SSD-backed or fully cached storage, or switching deep pages to keyset pagination, would fix it. Neither is part of this change.

```sql
SELECT m."Id", m."Name", m."Description", m."Price", m."ImageUrl", m."Type", m."CreatedAtUtc", m."UpdatedAtUtc" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") ORDER BY m."Name", m."Id" LIMIT 20 OFFSET 3980
```

```text
Limit  (cost=629.72..629.77 rows=20 width=166) (actual time=6.260..6.263 rows=20 loops=1)
  Buffers: shared hit=267
  ->  Sort  (cost=619.77..632.32 rows=5020 width=166) (actual time=5.981..6.153 rows=4000 loops=1)
        Sort Key: "Name", "Id"
        Sort Method: quicksort  Memory: 1487kB
        Buffers: shared hit=267
        ->  Seq Scan on "MenuItems" m  (cost=0.00..311.20 rows=5020 width=166) (actual time=0.006..0.716 rows=5020 loops=1)
              Filter: (NOT "IsDeleted")
              Buffers: shared hit=261
Planning:
  Buffers: shared hit=176
Planning Time: 0.696 ms
Execution Time: 6.350 ms
```

## Supplementary, list type=Drink page 1: count

Unchanged and already optimal: an index-only scan of the existing (Type, lower(Name)) index.

```sql
SELECT count(*)::int FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND m."Type" = 'Drink'
```

```text
Aggregate  (cost=162.48..162.49 rows=1 width=4) (actual time=0.401..0.401 rows=1 loops=1)
  Buffers: shared hit=30
  ->  Index Only Scan using "IX_MenuItems_Type_LowerName" on "MenuItems" m  (cost=0.28..156.21 rows=2510 width=0) (actual time=0.030..0.289 rows=2510 loops=1)
        Index Cond: ("Type" = 'Drink'::text)
        Heap Fetches: 0
        Buffers: shared hit=30
Planning:
  Buffers: shared hit=152
Planning Time: 0.535 ms
Execution Time: 0.433 ms
```

## Supplementary, list type=Drink page 1: page (LIMIT 20 OFFSET 0)

Walks the new (Name, Id) index and skips non-drinks until it has 20 (0.38 ms, was 1.12 ms). With two types at about half the menu each, this is cheap enough that a separate (Type, Name) index is not justified, so it was not added.

```sql
SELECT m."Id", m."Name", m."Description", m."Price", m."ImageUrl", m."Type", m."CreatedAtUtc", m."UpdatedAtUtc" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND m."Type" = 'Drink' ORDER BY m."Name", m."Id" LIMIT 20 OFFSET 0
```

```text
Limit  (cost=0.28..9.25 rows=20 width=166) (actual time=0.020..0.361 rows=20 loops=1)
  Buffers: shared hit=563
  ->  Index Scan using "IX_MenuItems_Name_Id_Live" on "MenuItems" m  (cost=0.28..1126.27 rows=2510 width=166) (actual time=0.019..0.359 rows=20 loops=1)
        Filter: (("Type")::text = 'Drink'::text)
        Rows Removed by Filter: 2505
        Buffers: shared hit=563
Planning:
  Buffers: shared hit=177
Planning Time: 0.589 ms
Execution Time: 0.379 ms
```

## Search q=milk: count

A bitmap OR of the two trigram indexes finds the 1,275 candidate rows, which are then rechecked, instead of running ILIKE on all 5,020 rows. 5.0 ms before, 1.7 ms now. With about a quarter of the menu matching, most of the remaining time is fetching and rechecking those rows.

```sql
SELECT count(*)::int FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND (m."Name" ILIKE '%milk%' ESCAPE '\' OR m."Description" ILIKE '%milk%' ESCAPE '\')
```

```text
Aggregate  (cost=332.93..332.94 rows=1 width=4) (actual time=1.592..1.592 rows=1 loops=1)
  Buffers: shared hit=148
  ->  Bitmap Heap Scan on "MenuItems" m  (cost=49.90..329.78 rows=1259 width=0) (actual time=0.121..1.526 rows=1275 loops=1)
        Recheck Cond: (((("Name")::text ~~* '%milk%'::text) AND (NOT "IsDeleted")) OR ((("Description")::text ~~* '%milk%'::text) AND (NOT "IsDeleted")))
        Heap Blocks: exact=136
        Buffers: shared hit=148
        ->  BitmapOr  (cost=49.90..49.90 rows=1259 width=0) (actual time=0.106..0.107 rows=0 loops=1)
              Buffers: shared hit=12
              ->  Bitmap Index Scan on "IX_MenuItems_Name_Trigram"  (cost=0.00..21.49 rows=1 width=0) (actual time=0.017..0.017 rows=2 loops=1)
                    Index Cond: (("Name")::text ~~* '%milk%'::text)
                    Buffers: shared hit=5
              ->  Bitmap Index Scan on "IX_MenuItems_Description_Trigram"  (cost=0.00..27.77 rows=1259 width=0) (actual time=0.089..0.089 rows=1275 loops=1)
                    Index Cond: (("Description")::text ~~* '%milk%'::text)
                    Buffers: shared hit=7
Planning:
  Buffers: shared hit=163
Planning Time: 0.954 ms
Execution Time: 1.673 ms
```

## Search q=milk: page (LIMIT 20 OFFSET 0)

The planner now walks the (Name, Id) index in order and tests ILIKE only until it has 20 matches (78 rows checked), which beats using the trigram indexes when a term matches this many items. 5.2 ms before, 0.16 ms now. For rarer terms the planner can switch to the trigram indexes, as the count query does.

```sql
SELECT m."Id", m."Name", m."Description", m."Price", m."ImageUrl", m."Type", m."CreatedAtUtc", m."UpdatedAtUtc" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND (m."Name" ILIKE '%milk%' ESCAPE '\' OR m."Description" ILIKE '%milk%' ESCAPE '\') ORDER BY m."Name", m."Id" LIMIT 20 OFFSET 0
```

```text
Limit  (cost=0.28..18.37 rows=20 width=166) (actual time=0.023..0.122 rows=20 loops=1)
  Buffers: shared hit=22
  ->  Index Scan using "IX_MenuItems_Name_Id_Live" on "MenuItems" m  (cost=0.28..1138.82 rows=1259 width=166) (actual time=0.022..0.120 rows=20 loops=1)
        Filter: ((("Name")::text ~~* '%milk%'::text) OR (("Description")::text ~~* '%milk%'::text))
        Rows Removed by Filter: 58
        Buffers: shared hit=22
Planning:
  Buffers: shared hit=194
Planning Time: 1.196 ms
Execution Time: 0.160 ms
```

## View item by id

A single primary key lookup that reads the stored AverageRating and RatingCount. Both correlated subqueries and their joins are gone: execution fell from 0.26 to 0.08 ms and planning from 1.1 to 0.7 ms, and the cost no longer depends on how many ratings an item has.

```sql
SELECT m."Id", m."Name", m."Description", m."Price", m."ImageUrl", m."Type", m."AverageRating"::double precision, m."RatingCount", m."CreatedAtUtc", m."UpdatedAtUtc" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND m."Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f' LIMIT 1
```

```text
Limit  (cost=0.28..8.30 rows=1 width=178) (actual time=0.035..0.035 rows=1 loops=1)
  Buffers: shared hit=3
  ->  Index Scan using "PK_MenuItems" on "MenuItems" m  (cost=0.28..8.30 rows=1 width=178) (actual time=0.034..0.034 rows=1 loops=1)
        Index Cond: ("Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
        Filter: (NOT "IsDeleted")
        Buffers: shared hit=3
Planning:
  Buffers: shared hit=172
Planning Time: 0.688 ms
Execution Time: 0.082 ms
```

## Item ratings: existence check

Unchanged: a single primary key lookup.

```sql
SELECT EXISTS (SELECT 1 FROM "MenuItems" AS m WHERE NOT (m."IsDeleted") AND m."Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f')
```

```text
Result  (cost=8.30..8.31 rows=1 width=1) (actual time=0.038..0.038 rows=1 loops=1)
  Buffers: shared hit=3
  InitPlan 1 (returns $0)
    ->  Index Scan using "PK_MenuItems" on "MenuItems" m  (cost=0.28..8.30 rows=1 width=0) (actual time=0.036..0.036 rows=1 loops=1)
          Index Cond: ("Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
          Filter: (NOT "IsDeleted")
          Buffers: shared hit=3
Planning:
  Buffers: shared hit=151
Planning Time: 0.521 ms
Execution Time: 0.101 ms
```

## Item ratings: count

Unchanged: a primary key lookup plus an index-only scan on the unique (MenuItemId, UserId) index.

```sql
SELECT count(*)::int FROM "Ratings" AS r INNER JOIN (SELECT m."Id", m."IsDeleted" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted")) AS m0 ON r."MenuItemId" = m0."Id" WHERE NOT (m0."IsDeleted") AND r."MenuItemId" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'
```

```text
Aggregate  (cost=12.64..12.66 rows=1 width=4) (actual time=0.040..0.040 rows=1 loops=1)
  Buffers: shared hit=6
  ->  Nested Loop  (cost=0.57..12.64 rows=2 width=0) (actual time=0.037..0.038 rows=1 loops=1)
        Buffers: shared hit=6
        ->  Index Scan using "PK_MenuItems" on "MenuItems" m  (cost=0.28..8.30 rows=1 width=16) (actual time=0.019..0.019 rows=1 loops=1)
              Index Cond: ("Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
              Filter: ((NOT "IsDeleted") AND (NOT "IsDeleted"))
              Buffers: shared hit=3
        ->  Index Only Scan using "IX_Ratings_MenuItemId_UserId" on "Ratings" r  (cost=0.29..4.32 rows=2 width=16) (actual time=0.017..0.017 rows=1 loops=1)
              Index Cond: ("MenuItemId" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
              Heap Fetches: 0
              Buffers: shared hit=3
Planning:
  Buffers: shared hit=279
Planning Time: 0.848 ms
Execution Time: 0.094 ms
```

## Item ratings: page (LIMIT 20 OFFSET 0)

Unchanged, as expected, because no rating index was added. With a couple of ratings per item the unique (MenuItemId, UserId) index plus a tiny sort stays cheap, which is why a descending (MenuItemId, CreatedAtUtc) index was left out.

```sql
SELECT s."Id", s."MenuItemId", u."Name", s."Stars", s."Comment", s."CreatedAtUtc", s."UpdatedAtUtc" FROM (SELECT r."Id", r."Comment", r."CreatedAtUtc", r."MenuItemId", r."Stars", r."UpdatedAtUtc", r."UserId" FROM "Ratings" AS r INNER JOIN (SELECT m."Id", m."IsDeleted" FROM "MenuItems" AS m WHERE NOT (m."IsDeleted")) AS m0 ON r."MenuItemId" = m0."Id" WHERE NOT (m0."IsDeleted") AND r."MenuItemId" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f' ORDER BY r."CreatedAtUtc" DESC, r."Id" DESC LIMIT 20 OFFSET 0) AS s INNER JOIN "Users" AS u ON s."UserId" = u."Id" ORDER BY s."CreatedAtUtc" DESC, s."Id" DESC
```

```text
Sort  (cost=27.81..27.82 rows=2 width=133) (actual time=0.111..0.112 rows=1 loops=1)
  Sort Key: s."CreatedAtUtc" DESC, s."Id" DESC
  Sort Method: quicksort  Memory: 25kB
  Buffers: shared hit=17
  ->  Hash Join  (cost=20.01..27.80 rows=2 width=133) (actual time=0.049..0.080 rows=1 loops=1)
        Hash Cond: (u."Id" = s."UserId")
        Buffers: shared hit=11
        ->  Seq Scan on "Users" u  (cost=0.00..7.02 rows=202 width=38) (actual time=0.002..0.016 rows=202 loops=1)
              Buffers: shared hit=5
        ->  Hash  (cost=19.98..19.98 rows=2 width=127) (actual time=0.038..0.039 rows=1 loops=1)
              Buckets: 1024  Batches: 1  Memory Usage: 9kB
              Buffers: shared hit=6
              ->  Subquery Scan on s  (cost=19.96..19.98 rows=2 width=127) (actual time=0.034..0.035 rows=1 loops=1)
                    Buffers: shared hit=6
                    ->  Limit  (cost=19.96..19.96 rows=2 width=127) (actual time=0.033..0.034 rows=1 loops=1)
                          Buffers: shared hit=6
                          ->  Sort  (cost=19.96..19.96 rows=2 width=127) (actual time=0.032..0.033 rows=1 loops=1)
                                Sort Key: r."CreatedAtUtc" DESC, r."Id" DESC
                                Sort Method: quicksort  Memory: 25kB
                                Buffers: shared hit=6
                                ->  Nested Loop  (cost=4.58..19.95 rows=2 width=127) (actual time=0.024..0.025 rows=1 loops=1)
                                      Buffers: shared hit=6
                                      ->  Index Scan using "PK_MenuItems" on "MenuItems" m  (cost=0.28..8.30 rows=1 width=16) (actual time=0.014..0.014 rows=1 loops=1)
                                            Index Cond: ("Id" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
                                            Filter: ((NOT "IsDeleted") AND (NOT "IsDeleted"))
                                            Buffers: shared hit=3
                                      ->  Bitmap Heap Scan on "Ratings" r  (cost=4.30..11.63 rows=2 width=127) (actual time=0.008..0.009 rows=1 loops=1)
                                            Recheck Cond: ("MenuItemId" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
                                            Heap Blocks: exact=1
                                            Buffers: shared hit=3
                                            ->  Bitmap Index Scan on "IX_Ratings_MenuItemId_UserId"  (cost=0.00..4.30 rows=2 width=0) (actual time=0.006..0.006 rows=1 loops=1)
                                                  Index Cond: ("MenuItemId" = '01a0d8fc-3fc7-7d48-a7da-bd132ac8855f'::uuid)
                                                  Buffers: shared hit=2
Planning:
  Buffers: shared hit=352
Planning Time: 1.397 ms
Execution Time: 0.185 ms
```
