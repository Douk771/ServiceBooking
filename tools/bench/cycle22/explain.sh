#!/bin/bash
# EXPLAIN (ANALYZE, BUFFERS) of the hot Bookings predicates (F5 index) on both DBs.
# Each query is run 3 times, the last plan is kept (warm cache). Output: /tmp/bench/results/explain-<v>.txt
export PGPASSWORD=postgres
for v in before after; do
  out=/tmp/bench/results/explain-$v.txt
  : > $out
  M=$(psql -h localhost -U postgres -d bench_$v -Atc "select \"Id\" from \"AspNetUsers\" where \"PhoneNumber\"='79000000101'")
  C=$(psql -h localhost -U postgres -d bench_$v -Atc "select \"Id\" from \"Companies\" where \"Slug\"='bench-salon'")
  while IFS='|' read -r name sql; do
    [ -z "$name" ] && continue
    sql=${sql//:M/\'$M\'}; sql=${sql//:C/\'$C\'}
    for i in 1 2; do psql -h localhost -U postgres -d bench_$v -Atc "EXPLAIN (ANALYZE, BUFFERS) $sql" > /dev/null; done
    { echo "### $name"; echo "$sql"; psql -h localhost -U postgres -d bench_$v -Atc "EXPLAIN (ANALYZE, BUFFERS) $sql"; echo; } >> $out
  done <<'EOF'
slots 1 day (occupancy)|SELECT b."StartTime", b."EndTime" FROM "Bookings" b WHERE b."MasterId" = :M AND b."Date" = '2026-09-30' AND b."Status" <> 2
availability 30 days|SELECT b."Date", b."StartTime", b."EndTime" FROM "Bookings" b WHERE b."MasterId" = :M AND b."Date" >= '2026-09-28' AND b."Date" <= '2026-10-27' AND b."Status" <> 2
bookings/master 1 month (rows)|SELECT b.* FROM "Bookings" b WHERE b."MasterId" = :M AND b."Date" >= '2026-09-01' AND b."Date" <= '2026-09-30' ORDER BY b."Date", b."StartTime"
masters/clients grouping (MasterId+CompanyId)|SELECT b."ClientId", max(b."Date"), count(*) FROM "Bookings" b WHERE b."MasterId" = :M AND b."CompanyId" = :C AND b."ClientId" IS NOT NULL GROUP BY b."ClientId"
stats: status counts 1 year (CompanyId+Date)|SELECT b."Status", count(*) FROM "Bookings" b WHERE b."CompanyId" = :C AND b."Date" >= '2025-09-01' AND b."Date" <= '2026-08-31' GROUP BY b."Status"
EOF
done
grep -E "^###|Index|Seq Scan|Bitmap|Execution Time" /tmp/bench/results/explain-before.txt /tmp/bench/results/explain-after.txt
