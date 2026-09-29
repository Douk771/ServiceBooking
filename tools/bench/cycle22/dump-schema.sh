#!/bin/bash
# Dumps column lists of both bench DBs and diffs them.
export PGPASSWORD=postgres
for d in before after; do
  psql -h localhost -U postgres -d bench_$d -Atc "select table_name||'.'||column_name||':'||data_type||':'||is_nullable||':'||coalesce(column_default,'') from information_schema.columns where table_schema='public' order by table_name, ordinal_position" > /tmp/bench/schema-$d.txt
done
diff /tmp/bench/schema-before.txt /tmp/bench/schema-after.txt
