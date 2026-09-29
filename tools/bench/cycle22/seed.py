#!/usr/bin/env python3
"""Seeds one bench DB (bench_before / bench_after) with an identical, deterministic dataset.

Usage: seed.py <before|after> <api-port>

Phase A (public API): owner registration, company, 8 masters (register + add member), 10 services,
one "bench client" account. Phase B (direct SQL, COPY): tariff/subscription, ~3000 client accounts,
~1500 guest phones, ~40k bookings over 2 years (+30 days ahead), BookingServices (10% multi-service),
BookingEvents, client notes (~500 clients), reminder OutboundNotifications (+-30 days), working hours
and breaks, 300 filler companies/owners, 250 notification channels.

All random choices come from random.Random(SEED) and a fixed anchor date, so both DBs get the same rows;
only ids minted by the API (users/company/services created in phase A) differ, and bulk rows refer to
them through the phone/slug/name map read back from each DB.
"""
import datetime as dt
import json
import random
import subprocess
import sys
import uuid

import requests

SEED = 20260928
ANCHOR = dt.date(2026, 9, 28)          # "today" for the generator (fixed, both DBs)
PASSWORD = "Password123!"
OWNER_PHONE = "+79000000001"
MASTER_PHONES = [f"+790000001{i:02d}" for i in range(1, 9)]
CLIENT_PHONE = "+79000000201"
SLUG = "bench-salon"
SERVICES = [  # name, minutes, price
    ("Стрижка женская", 60, 2500), ("Стрижка мужская", 30, 1200), ("Окрашивание", 60, 4500),
    ("Укладка", 30, 1500), ("Маникюр", 60, 2000), ("Педикюр", 60, 2800), ("Брови", 30, 900),
    ("Ресницы", 60, 3000), ("Бритьё", 30, 1000), ("Уход за волосами", 30, 1800),
]
HOURS = [9, 10, 11, 12, 14, 15, 16, 17, 18, 19]
FIRST = ["Анна", "Мария", "Елена", "Ольга", "Ирина", "Наталья", "Татьяна", "Светлана", "Иван", "Пётр",
         "Алексей", "Дмитрий", "Сергей", "Андрей", "Юлия", "Ксения", "Виктория", "Павел", "Никита", "Олег"]
LAST = ["Иванова", "Петрова", "Смирнова", "Кузнецова", "Попова", "Соколова", "Лебедева", "Козлова",
        "Новикова", "Морозова", "Волкова", "Соловьёва", "Васильева", "Зайцева", "Павлова", "Семёнова"]


def psql(db, sql=None, file=None):
    args = ["psql", "-h", "localhost", "-U", "postgres", "-d", db, "-v", "ON_ERROR_STOP=1", "-q", "-At"]
    args += ["-f", file] if file else ["-c", sql]
    r = subprocess.run(args, capture_output=True, text=True, env={"PGPASSWORD": "postgres", "PATH": "/usr/bin:/bin"})
    if r.returncode != 0:
        raise SystemExit(f"psql failed: {r.stderr[:2000]}")
    return r.stdout.strip()


def main():
    version, port = sys.argv[1], sys.argv[2]
    db = f"bench_{version}"
    base = f"http://127.0.0.1:{port}"
    s = requests.Session()

    if psql(db, f"select count(*) from \"Companies\" where \"Slug\"='{SLUG}'") != "0":
        raise SystemExit("already seeded")

    docs = {d["type"]: d["version"] for d in s.get(f"{base}/api/legal/documents").json()["documents"]}

    def register(phone, first, last):
        r = s.post(f"{base}/api/auth/register", json={
            "firstName": first, "lastName": last, "phone": phone, "password": PASSWORD, "email": None,
            "legal": {"privacyAcknowledgedVersion": docs["Privacy"], "termsAcceptedVersion": docs["TermsClient"]}})
        r.raise_for_status()
        return r.json()

    def login(phone):
        r = s.post(f"{base}/api/auth/login", json={"phone": phone, "password": PASSWORD})
        r.raise_for_status()
        return r.json()["token"]

    def auth(tok):
        return {"Authorization": f"Bearer {tok}"}

    # ---- Phase A: public API ----
    owner = register(OWNER_PHONE, "Ольга", "Владелица")
    city_id = int(psql(db, "select min(\"Id\") from \"Cities\" where \"IsActive\""))
    r = s.post(f"{base}/api/companies", headers=auth(owner["token"]), json={
        "name": "Bench Salon", "slug": SLUG, "description": None, "address": None, "phone": None,
        "email": "salon@bench.local", "cityId": city_id, "timeZoneId": None, "allowSelfBooking": True,
        "showInPublicListing": True, "ownerTerms": {"version": docs["TermsOwner"]}})
    r.raise_for_status()
    company_id = r.json()["company"]["id"]

    # Tariff: same shape as ApiTestBase.GiveAccountPlanAsync (arrange-only, written straight to DB).
    psql(db, f"""
        insert into "SubscriptionPlanConfigs" ("Id","Name","PricePerMonth","MaxEmployees","AllowOnlineBooking",
          "AllowMailing","AllowAnalytics","Description","IsActive","NotifyDaysBefore","CreatedAt","MaxCompanies",
          "AllowOnlinePayment","AllowPublicListing","PhotoQuotaMb","PhotoRetention","AllowNotificationChannel",
          "IsPublic","IsSystemFree","SortOrder","IsSystemTrial")
        values ('11111111-1111-1111-1111-111111111111','Bench Full',1,null,true,true,true,null,true,3,now(),null,
          true,true,1000,0,true,false,false,0,false);
        insert into "AccountSubscriptions" ("Id","OwnerUserId","PlanConfigId","PaidUntil","IsActive","CreatedAt",
          "UpdatedAt","BillingAccountId")
        select '22222222-2222-2222-2222-222222222222', c."OwnerUserId", '11111111-1111-1111-1111-111111111111',
          now() + interval '1 year', true, now(), now(), c."BillingAccountId"
        from "Companies" c where c."Id"='{company_id}'
          and not exists (select 1 from "AccountSubscriptions" s where s."OwnerUserId"=c."OwnerUserId");
        update "AccountSubscriptions" s set "PlanConfigId"='11111111-1111-1111-1111-111111111111',
          "PaidUntil"=now() + interval '1 year', "IsActive"=true
        from "Companies" c where c."Id"='{company_id}' and s."OwnerUserId"=c."OwnerUserId";
    """)
    owner_tok = login(OWNER_PHONE)
    for i, phone in enumerate(MASTER_PHONES):
        first, last = FIRST[i], "Мастерова"
        register(phone, first, last)
        r = s.post(f"{base}/api/companies/{company_id}/members", headers=auth(owner_tok), json={
            "phone": phone, "firstName": first, "lastName": last, "role": "Master", "bio": None, "email": None})
        r.raise_for_status()
    for name, minutes, price in SERVICES:
        r = s.post(f"{base}/api/services", headers=auth(owner_tok), json={
            "companyId": company_id, "name": name, "description": None, "durationMinutes": minutes, "price": price})
        r.raise_for_status()
    register(CLIENT_PHONE, "Клиент", "Бенчев")

    # ---- map API-minted ids ----
    rows = psql(db, "select \"PhoneNumber\", \"Id\" from \"AspNetUsers\" where \"PhoneNumber\" like '7900000%'")
    uid = dict(line.split("|") for line in rows.splitlines())
    owner_id = uid[OWNER_PHONE[1:]]
    masters = [uid[p[1:]] for p in MASTER_PHONES]
    bench_client = uid[CLIENT_PHONE[1:]]
    rows = psql(db, f"select \"Name\", \"Id\" from \"Services\" where \"CompanyId\"='{company_id}'")
    svc_id = dict(line.split("|") for line in rows.splitlines())
    billing = psql(db, f"select \"BillingAccountId\" from \"Companies\" where \"Id\"='{company_id}'")

    # ---- Phase B: deterministic bulk ----
    rng = random.Random(SEED)
    U = lambda: str(uuid.UUID(int=rng.getrandbits(128), version=4))
    N = "\\N"
    out = []

    def copy(table, cols, data):
        out.append(f'COPY "{table}" ({",".join(chr(34) + c + chr(34) for c in cols)}) FROM stdin;')
        for row in data:
            out.append("\t".join(N if v is None else str(v) for v in row))
        out.append("\\.")

    ts = lambda d, h=10, m=0: f"{d.isoformat()} {h:02d}:{m:02d}:00+00"

    # client accounts
    user_cols = ["Id", "FirstName", "LastName", "CreatedAt", "UserName", "NormalizedUserName", "Email",
                 "NormalizedEmail", "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
                 "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount"]
    clients, users = [], []
    for i in range(3000):
        cid = f"bench-client-{i:05d}"
        phone = f"7911{i:07d}"
        users.append([cid, rng.choice(FIRST), rng.choice(LAST), ts(ANCHOR - dt.timedelta(days=rng.randint(30, 900))),
                      phone, phone, None, None, "f", None, U().upper(), U(), phone,
                      "t" if rng.random() < 0.5 else "f", "f", "t", 0])
        clients.append(cid)
    guests = [(f"7922{i:07d}", f"{rng.choice(FIRST)} {rng.choice(LAST)}") for i in range(1500)]

    # filler companies/owners (admin lists)
    filler_owners, companies, members, accounts, subs = [], [], [], [], []
    for i in range(300):
        oid = f"bench-owner-{i:04d}"
        phone = f"7933{i:07d}"
        users.append([oid, rng.choice(FIRST), rng.choice(LAST), ts(ANCHOR - dt.timedelta(days=900 - i)),
                      phone, phone, f"owner{i}@bench.local", f"OWNER{i}@BENCH.LOCAL", "f", None, U().upper(), U(),
                      phone, "f", "f", "t", 0])
        acc = U()
        accounts.append([acc, oid, 0, ts(ANCHOR - dt.timedelta(days=900 - i)), ts(ANCHOR)])
        cmp_id = U()
        companies.append([cmp_id, f"Filler {i:03d}", f"filler-{i:03d}", f"filler{i}@bench.local", "t", "t",
                          ts(ANCHOR - dt.timedelta(days=900 - i)), oid, city_id, acc])
        members.append([U(), cmp_id, oid, 2, ts(ANCHOR - dt.timedelta(days=900 - i))])
        for j in rng.sample(range(3000), rng.randint(0, 4)):
            members.append([U(), cmp_id, f"bench-client-{j:05d}", 1, ts(ANCHOR)])
        filler_owners.append((oid, acc, cmp_id))
    copy("AspNetUsers", user_cols, users)
    copy("BillingAccounts", ["Id", "OwnerUserId", "GrandfatheredEmployeeBonus", "CreatedAtUtc", "UpdatedAtUtc"], accounts)
    copy("Companies", ["Id", "Name", "Slug", "Email", "AllowSelfBooking", "IsActive", "CreatedAt", "OwnerUserId",
                       "CityId", "BillingAccountId"], companies)
    copy("CompanyMembers", ["Id", "CompanyId", "UserId", "Role", "JoinedAt"], members)

    channels, assigns = [], []
    for i, (oid, acc, cmp_id) in enumerate(filler_owners[:250]):
        ch = U()
        channels.append([ch, oid, i % 2, rng.choice([0, 1, 2, 2, 2, 3, 6]), f"7944{i:07d}", "f", 0,
                         ts(ANCHOR - dt.timedelta(days=rng.randint(1, 400))), acc,
                         ts(ANCHOR - dt.timedelta(days=rng.randint(1, 30))) if rng.random() < 0.2 else None])
        assigns.append([U(), ch, cmp_id, ts(ANCHOR - dt.timedelta(days=10)), oid, acc, i % 2])
    copy("NotificationChannels", ["Id", "OwnerUserId", "Transport", "State", "PhoneNumber", "IsSuspendedByAdmin",
                                  "ConsecutiveSendFailures", "CreatedAt", "BillingAccountId", "RequestedAtUtc"], channels)
    copy("ChannelCompanyAssignments", ["Id", "ChannelId", "CompanyId", "AssignedAtUtc", "AssignedByUserId",
                                       "BillingAccountId", "Transport"], assigns)

    # working hours + breaks (T-1 .. T+45)
    wh, br = [], []
    for m in masters:
        for k in range(-1, 46):
            d = ANCHOR + dt.timedelta(days=k)
            working = d.weekday() != 6
            wid = U()
            wh.append([wid, m, company_id, "09:00:00", "21:00:00", "t" if working else "f", d.isoformat()])
            if working:
                br.append([U(), wid, "13:00:00", "14:00:00"])
    copy("WorkingHours", ["Id", "MasterId", "CompanyId", "StartTime", "EndTime", "IsWorking", "Date"], wh)
    copy("ScheduleBreaks", ["Id", "WorkingHoursId", "StartTime", "EndTime"], br)

    # bookings
    cum, acc_w = [], 0.0
    for i in range(len(clients)):  # a few regulars, a long tail
        acc_w += 1.0 / (i + 1) ** 0.7
        cum.append(acc_w)
    short = [s_ for s_ in SERVICES if s_[1] == 30]
    bookings, bsvc, events, notifs = [], [], [], []
    seq = 0
    for k in range(-730, 31):
        d = ANCHOR + dt.timedelta(days=k)
        for mi, m in enumerate(masters):
            n = rng.randint(5, 8) if k < 0 else rng.randint(2, 5)
            for h in sorted(rng.sample(HOURS, n)):
                seq += 1
                bid = U()
                if rng.random() < 0.10:
                    picked = rng.sample(short, 2)
                else:
                    picked = [rng.choice(SERVICES)]
                dur = sum(p[1] for p in picked)
                price = sum(p[2] for p in picked)
                if seq % 260 == 0:
                    client, gphone, gname = bench_client, None, None
                elif rng.random() < 0.75:
                    client, gphone, gname = rng.choices(clients, cum_weights=cum)[0], None, None
                else:
                    client = None
                    gphone, gname = rng.choice(guests)
                x = rng.random()
                if k < 0:
                    status = 3 if x < 0.80 else 2 if x < 0.90 else 4 if x < 0.95 else 1
                else:
                    status = 1 if x < 0.70 else 0 if x < 0.95 else 2
                created = d - dt.timedelta(days=rng.randint(0, 14))
                end_min = h * 60 + dur
                bookings.append([bid, company_id, svc_id[picked[0][0]], m, client, gname, gphone, None,
                                 d.isoformat(), f"{h:02d}:00:00", f"{end_min // 60:02d}:{end_min % 60:02d}:00",
                                 status, "Пожелание клиента" if rng.random() < 0.1 else None,
                                 "Клиент отменил" if status == 2 else None, ts(created), ts(created),
                                 0, price, 30 + 5 * mi, "f", "f"])
                for pos, p in enumerate(picked):
                    bsvc.append([U(), bid, svc_id[p[0]], pos, p[0], p[1], p[2]])
                events.append([U(), bid, company_id, 0, ts(created), 2, m, None])
                if status in (2, 3, 4):
                    events.append([U(), bid, company_id, {2: 2, 3: 3, 4: 4}[status], ts(d, 20), 2, m, None])
                if -30 <= k <= 30 and status != 2:
                    visit = dt.datetime(d.year, d.month, d.day, h) - dt.timedelta(hours=3)
                    due = visit - dt.timedelta(hours=24)
                    st = 0 if k > 0 else rng.choice([1, 2, 2, 2, 3])
                    body = ("Напоминаем о записи в Bench Salon " + d.isoformat() + f" в {h:02d}:00. "
                            + "Если планы изменились, пожалуйста, отмените запись по ссылке. " * 5)
                    notifs.append([U(), company_id, bid, 1, gphone or "79110000000", gname, client, body,
                                   visit.isoformat() + "+00", due.isoformat() + "+00",
                                   st, 1 if st else 0, 1, f"bench-{bid}", ts(created), 0])
    copy("Bookings", ["Id", "CompanyId", "ServiceId", "MasterId", "ClientId", "GuestName", "GuestPhone", "GuestEmail",
                      "Date", "StartTime", "EndTime", "Status", "Notes", "CancellationReason", "CreatedAt", "UpdatedAt",
                      "PaymentStatus", "Price", "CommissionPercent", "ClientDeleted", "BookedForOther"], bookings)
    copy("BookingServices", ["Id", "BookingId", "ServiceId", "Position", "NameSnapshot", "DurationMinutes", "Price"], bsvc)
    copy("BookingEvents", ["Id", "BookingId", "CompanyId", "Kind", "OccurredAtUtc", "ActorKind", "ActorUserId",
                           "ActorNameSnapshot"], events)
    copy("OutboundNotifications", ["Id", "CompanyId", "BookingId", "Type", "RecipientPhone", "RecipientName",
                                   "RecipientUserId", "Body", "VisitStartUtc", "DueAtUtc", "Status", "AttemptCount",
                                   "Generation", "IdempotencyKey", "CreatedAt", "Transport"], notifs)

    # client notes: ~500 clients (400 registered + 100 guests) that actually booked here
    booked_clients = sorted({b[4] for b in bookings if b[4]})
    booked_guests = sorted({b[6] for b in bookings if b[6]})
    notes = []
    for key in rng.sample(booked_clients, 400) + rng.sample(booked_guests, 100):
        is_guest = key.startswith("7922")
        for _ in range(rng.randint(1, 6)):
            notes.append([U(), rng.choice(masters), None if is_guest else key, key if is_guest else None,
                          "Заметка мастера: предпочитает тёплую воду, аллергия на лак. " * rng.randint(1, 3),
                          ts(ANCHOR - dt.timedelta(days=rng.randint(0, 700)), rng.randint(8, 20), rng.randint(0, 59)),
                          company_id])
    copy("ClientNotes", ["Id", "MasterId", "ClientId", "GuestPhone", "Note", "CreatedAt", "CompanyId"], notes)

    path = f"/tmp/bench/seed-{version}.sql"
    with open(path, "w") as f:
        f.write("BEGIN;\n" + "\n".join(out) + "\nCOMMIT;\nANALYZE;\n")
    psql(db, file=path)
    print(json.dumps({"company": company_id, "bookings": len(bookings), "bookingServices": len(bsvc),
                      "events": len(events), "reminders": len(notifs), "notes": len(notes), "users": len(users),
                      "noteClients": 500}))


if __name__ == "__main__":
    main()
