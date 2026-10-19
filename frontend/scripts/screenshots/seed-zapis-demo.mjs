// Засев демо-салона «Записи» для съёмки скриншотов главной ezbook.ru. Тот же подход, что у goods (seed-goods-demo.mjs):
// только существующий HTTP API локального стека sb-shots, те же замки на адрес. Пишет .state/zapis-seed.json.
// Коды выхода: 0 ок; 1 непредвиденное; 2 база уже засеяна; 3 адрес не локальный/не Development; 5 сервер ответил не так,
// как ожидалось, или самопроверка не прошла.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import process from 'node:process'
import {
  ExitError, checkApiUrl, dateInZone, isSwaggerUiHtml, makeClient, makePassword, waitReady,
} from './seed-goods-demo.mjs'
import { BOOKINGS, CLIENT_SHOT, LOGO_IMAGE, MASTERS, OWNER, SALON, SERVICES, WORK_END, WORK_START } from './zapis-demo-data.mjs'

const HELP = `Засев демо-салона «Записи» для съёмки скриншотов.

Использование: node scripts/screenshots/seed-zapis-demo.mjs [--help]   (или npm run shots:zapis:seed)

Переменные окружения: SHOTS_API_URL, SHOTS_ALLOW_HOST, SHOTS_CITY — как у shots:seed (README.md).
Записи ставятся на завтра по времени салона, поэтому окна съёмки по часам нет.
Сначала поднимите стек: frontend/scripts/screenshots/stack.sh reset (можно тот же, что для goods).

Коды выхода: 0 ок; 1 непредвиденное; 2 база уже засеяна (stack.sh reset); 3 адрес не локальный или не
Development; 5 ответ сервера не тот или самопроверка не прошла.
`

const out = (s) => process.stdout.write(`${s}\n`)
const isoNoMs = (d) => d.toISOString().replace(/\.\d+Z$/, 'Z')
const SCHEDULE_DAYS = 14
const ASSETS = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', 'ServiceBooking.API', 'ShowcaseAssets')

/** multipart-загрузка картинки (поле file), как это делает кабинет. */
async function upload(base, path, token, assetPath) {
  const form = new FormData()
  const name = assetPath.split('/').pop()
  form.append('file', new Blob([readFileSync(join(ASSETS, assetPath))], { type: 'image/jpeg' }), name)
  const res = await globalThis.fetch(`${base}${path}`, { method: 'POST', headers: { Authorization: `Bearer ${token}` }, body: form })
  if (!res.ok) throw new ExitError(5, `POST ${path} -> ${res.status}\n${await res.text()}`)
}

/** YYYY-MM-DD + n дней (календарно, без часовых поясов). */
export function addDays(ymd, n) {
  const d = new Date(`${ymd}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + n)
  return d.toISOString().slice(0, 10)
}

export async function main(env = process.env) {
  const base = checkApiUrl(env.SHOTS_API_URL || 'http://localhost:55000', env.SHOTS_ALLOW_HOST || '')
  const call = makeClient(base)

  await waitReady(call)
  try {
    const html = await call('GET', '/swagger/index.html', { expected: [200] })
    if (!isSwaggerUiHtml(html)) throw new Error('not swagger ui')
  } catch {
    throw new ExitError(3, 'Это не стенд разработки: /swagger/index.html не отдаёт Swagger UI (возможно, SPA-фолбэк боевого хоста).')
  }

  const cityName = env.SHOTS_CITY || 'Москва'
  const cities = await call('GET', `/api/cities?search=${encodeURIComponent(cityName)}`)
  const city = (cities.items ?? cities).find((c) => c.name === cityName)
  if (!city) throw new ExitError(5, `Город «${cityName}» не найден в /api/cities`)

  const legal = await call('GET', '/api/legal/documents')
  const version = (type) => {
    const d = legal.documents.find((x) => x.type === type)
    if (!d) throw new ExitError(5, `В /api/legal/documents нет документа ${type}`)
    return d.version
  }

  const password = makePassword()
  let auth
  try {
    auth = await call('POST', '/api/auth/register', {
      body: {
        firstName: OWNER.firstName, lastName: OWNER.lastName, phone: OWNER.phone, password, email: null,
        legal: { privacyAcknowledgedVersion: version('Privacy'), termsAcceptedVersion: version('TermsClient') },
      },
    })
  } catch (e) {
    if (e.code === 5 && e.status === 400 && /Duplicate|уже|занят|taken/i.test(e.body || '')) {
      throw new ExitError(2, 'База уже засеяна салоном — выполните stack.sh reset.')
    }
    throw e
  }
  if (!auth?.token) auth = await call('POST', '/api/auth/login', { body: { phone: OWNER.phone, password } })
  let token = auth.token

  const created = await call('POST', '/api/Companies', {
    token,
    body: {
      name: SALON.name, slug: SALON.slug, description: SALON.description, address: SALON.address, phone: SALON.phone,
      email: null, cityId: city.id, timeZoneId: null, allowSelfBooking: true, showInPublicListing: true,
      ownerTerms: { version: version('TermsOwner') },
    },
  })
  if (created.token) token = created.token
  const company = created.company
  const companyId = company.id
  const timeZoneId = company.timeZoneId || city.timeZoneId

  const serviceIds = {}
  for (const s of SERVICES) {
    const res = await call('POST', '/api/Services', {
      token, body: { companyId, name: s.name, description: s.description, durationMinutes: s.durationMinutes, price: s.price },
    })
    serviceIds[s.key] = res.id
    await upload(base, `/api/Services/${res.id}/image`, token, `services/${s.image}`)
  }
  await upload(base, `/api/Companies/${companyId}/logo`, token, LOGO_IMAGE)

  for (const m of MASTERS) {
    if (m.key === 'owner') continue
    await call('POST', `/api/Companies/${companyId}/members`, {
      token, body: { phone: m.phone, firstName: m.firstName, lastName: m.lastName, role: 'Master', bio: m.bio, email: null },
    })
  }
  const members = await call('GET', `/api/Companies/${companyId}/members`, { token })
  const masterIds = {}
  for (const m of MASTERS) {
    const phone = m.key === 'owner' ? OWNER.phone : m.phone
    const digits = (v) => String(v).replace(/\D/g, '')
    const member = members.find((x) => digits(x.phone) === digits(phone))
    if (!member) throw new ExitError(5, `Сотрудник ${phone} не найден в /members: ${JSON.stringify(members.map((x) => [x.phone, x.role]))}`)
    masterIds[m.key] = member.userId
    if (!member.providesServices) {
      await call('PUT', `/api/Companies/${companyId}/members/${member.id}/provides-services`, {
        token, body: { providesServices: true, confirm: true },
      })
    }
    await call('PUT', `/api/Companies/${companyId}/members/${member.id}/services`, {
      token, body: m.services.map((k) => serviceIds[k]),
    })
  }

  const today = dateInZone(new Date(), timeZoneId)
  const shootDate = addDays(today, 1)
  for (let i = 0; i < SCHEDULE_DAYS; i++) {
    const date = addDays(today, i)
    for (const m of MASTERS) {
      await call('PUT', '/api/WorkingHours', {
        token,
        body: {
          masterId: masterIds[m.key], companyId, date, isWorking: true,
          startTime: `${WORK_START}:00`, endTime: `${WORK_END}:00`, breaks: [],
        },
      })
    }
  }

  for (const b of BOOKINGS) {
    await call('POST', '/api/Bookings', {
      body: {
        companyId, serviceId: serviceIds[b.service], masterId: masterIds[b.master], date: shootDate,
        startTime: `${b.time}:00`, notes: b.notes, guestName: b.name, guestPhone: b.phone, guestEmail: null,
        captchaToken: null, notifyByMessenger: false,
      },
    })
  }

  // ---------- самопроверка ----------
  const ownerBookings = await call('GET', `/api/Bookings/master?date=${shootDate}&to=${shootDate}`, { token })
  const expectedOwner = BOOKINGS.filter((b) => b.master === 'owner').length
  if (ownerBookings.length !== expectedOwner) {
    throw new ExitError(5, `Самопроверка: у владельца ${ownerBookings.length} записей на ${shootDate}, ожидалось ${expectedOwner}`)
  }

  const state = {
    schemaVersion: 1,
    seededAtUtc: isoNoMs(new Date()),
    apiUrl: base,
    company: { id: companyId, slug: SALON.slug, name: SALON.name, timeZoneId },
    owner: { phone: OWNER.phone, password },
    shootDate,
    // Кадр клиента — на следующий за днём съёмки день: записей там нет, свободное время видно целиком.
    clientDate: addDays(shootDate, 1),
    ownerClientNames: BOOKINGS.filter((b) => b.master === 'owner').map((b) => b.name),
    clientShot: {
      serviceName: SERVICES.find((s) => s.key === CLIENT_SHOT.service).name,
      masterName: `${OWNER.firstName} ${OWNER.lastName}`,
    },
    ownerBookingCount: expectedOwner,
  }
  const stateDir = join(dirname(fileURLToPath(import.meta.url)), '.state')
  mkdirSync(stateDir, { recursive: true })
  const stateFile = join(stateDir, 'zapis-seed.json')
  writeFileSync(stateFile, `${JSON.stringify(state, null, 2)}\n`, { mode: 0o600 })

  out(`Засев готов: «${SALON.name}», мастеров ${MASTERS.length}, услуг ${SERVICES.length}, записей ${BOOKINGS.length} на ${shootDate}.`)
  out(`Состояние записано: ${stateFile}`)
  out('Дальше: npm run shots:zapis:capture')
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  if (process.argv.includes('--help') || process.argv.includes('-h')) {
    process.stdout.write(HELP)
    process.exit(0)
  }
  main().then(
    () => process.exit(0),
    (e) => {
      if (e instanceof ExitError) {
        process.stderr.write(`[seed-zapis] ${e.message}\n`)
        process.exit(e.code)
      }
      process.stderr.write(`[seed-zapis] Непредвиденная ошибка: ${e?.stack ?? e}\n`)
      process.exit(1)
    },
  )
}
