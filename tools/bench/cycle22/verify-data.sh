#!/bin/bash
# Checks that both DBs hold the same seeded data: md5 over bookings/booking services/notes/reminders with
# API-minted ids (users, company, services) replaced by their stable keys (phone, service name).
export PGPASSWORD=postgres
Q='select md5(string_agg(concat_ws(\x27|\x27, b."Id", m."PhoneNumber", coalesce(c."PhoneNumber", b."ClientId"), b."GuestPhone", s."Name", b."Date", b."StartTime", b."EndTime", b."Status", b."Price", b."CommissionPercent"), \x27,\x27 order by b."Id")) || \x27 \x27 || count(*)
 from "Bookings" b join "AspNetUsers" m on m."Id"=b."MasterId" left join "AspNetUsers" c on c."Id"=b."ClientId" join "Services" s on s."Id"=b."ServiceId";
select md5(string_agg(concat_ws(\x27|\x27, bs."BookingId", bs."Position", bs."NameSnapshot", bs."Price"), \x27,\x27 order by bs."Id")) || \x27 \x27 || count(*) from "BookingServices" bs;
select md5(string_agg(concat_ws(\x27|\x27, n."Id", m."PhoneNumber", n."ClientId", n."GuestPhone", n."Note", n."CreatedAt"), \x27,\x27 order by n."Id")) || \x27 \x27 || count(*) from "ClientNotes" n join "AspNetUsers" m on m."Id"=n."MasterId";
select md5(string_agg(concat_ws(\x27|\x27, o."Id", o."BookingId", o."Status", o."Body"), \x27,\x27 order by o."Id")) || \x27 \x27 || count(*) from "OutboundNotifications" o;
select count(*) from "Companies";
select count(*) from "NotificationChannels";
select count(*) from "AspNetUsers";'
Q=$(printf "$Q")
for d in before after; do
  echo "== bench_$d"
  psql -h localhost -U postgres -d bench_$d -At -c "$Q"
done
