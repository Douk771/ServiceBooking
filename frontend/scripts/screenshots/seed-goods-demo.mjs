// Засев демо-данных goods для съёмки скриншотов (ARCHITECTURE_CYCLE30.md §30.7, API_CONTRACT_CYCLE30.md §30.21-30.22).
// Ходит только через существующий HTTP API локального стека sb-shots. Пишет .state/seed.json.
// Коды выхода: 0 ок; 1 непредвиденное; 2 база уже засеяна; 3 адрес не локальный/не Development;
// 4 время магазина вне [07:00, 20:30]; 5 сервер ответил не так, как ожидалось, или самопроверка не прошла.
import { randomBytes, randomUUID } from 'node:crypto'
import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import process from 'node:process'
import {
  ACCEPT_SEQUENCE, CATEGORIES, ORDERS, ORDER_PAGE_KEY, ORDER_SEQUENCE, OWNER, READY_SEQUENCE, SHOP,
} from './demo-data.mjs'

const HELP = `Засев демо-данных goods для съёмки скриншотов.

Использование: node scripts/screenshots/seed-goods-demo.mjs [--help]   (или npm run shots:seed)

Переменные окружения:
  SHOTS_API_URL      адрес API (по умолчанию http://localhost:55000)
  SHOTS_ALLOW_HOST   дополнительный разрешённый хост изолированного стенда
  SHOTS_CITY         город магазина (по умолчанию Москва)

Хост должен быть localhost/127.0.0.1/::1 или совпадать с SHOTS_ALLOW_HOST; *.ezbook.ru отклоняется всегда.
Перед записью проверяется, что GET /swagger/index.html отдаёт Swagger UI (он включён только в Development;
swagger.json не годится: в Development он отдаёт 500 из-за двух WorkingHoursDto).
Сначала поднимите стек: frontend/scripts/screenshots/stack.sh reset

Коды выхода: 0 ок; 1 непредвиденное; 2 база уже засеяна (stack.sh reset); 3 адрес не локальный или не
Development; 4 время магазина вне [07:00, 20:30]; 5 ответ сервера не тот или самопроверка не прошла.
`

class ExitError extends Error {
  constructor(code, message) {
    super(message)
    this.code = code
  }
}

const out = (s) => process.stdout.write(`${s}\n`)

// ---------- замки (до любого сетевого запроса) ----------

export function normalizeHost(h) {
  return String(h).trim().toLowerCase().replace(/^\[|\]$/g, '').replace(/\.+$/, '')
}

export function isSwaggerUiHtml(body) {
  if (typeof body !== 'string') return false
  return /swagger-ui|SwaggerUIBundle/i.test(body) && !/<div\s+id=["']root["']/i.test(body)
}

export function makePassword(bytes = randomBytes(18)) {
  return `${bytes.toString('base64url')}Aa1`
}

export function checkApiUrl(rawUrl, allowHost = '') {
  let url
  try {
    url = new URL(rawUrl)
  } catch {
    throw new ExitError(3, `SHOTS_API_URL не разобран как адрес: ${rawUrl}`)
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new ExitError(3, `Неподдерживаемая схема адреса: ${url.protocol}`)
  }
  const host = normalizeHost(url.hostname)
  if (host === 'ezbook.ru' || host.endsWith('.ezbook.ru')) {
    throw new ExitError(3, `Отказ: ${host} — боевой домен. Засев работает только с локальным стендом.`)
  }
  const local = ['localhost', '127.0.0.1', '::1']
  const allowed = normalizeHost(allowHost)
  if (!local.includes(host) && !(allowed && host === allowed)) {
    throw new ExitError(3, `Отказ: хост ${host} не в белом списке (localhost, 127.0.0.1, ::1 или SHOTS_ALLOW_HOST).`)
  }
  return url.origin
}

// ---------- время ----------

export function clockInZone(date, timeZone) {
  const parts = new Intl.DateTimeFormat('en-GB', { timeZone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(date)
  const get = (t) => parts.find((p) => p.type === t).value
  return { hour: Number(get('hour')), minute: Number(get('minute')), text: `${get('hour')}:${get('minute')}` }
}

export function dateInZone(date, timeZone) {
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(date)
}

export function isWithinShootingWindow(date, timeZone) {
  const { hour, minute } = clockInZone(date, timeZone)
  const minutes = hour * 60 + minute
  return minutes >= 7 * 60 && minutes <= 20 * 60 + 30
}

function assertShootingWindow(timeZone) {
  if (!isWithinShootingWindow(new Date(), timeZone)) {
    throw new ExitError(4, 'Слоты на +2 ч не поместятся в рабочие часы; запустите днём или задайте SHOTS_CITY с другим поясом.')
  }
}

// ---------- HTTP ----------

function makeClient(base) {
  return async function call(method, path, { body, token, expected = null } = {}) {
    const headers = { Accept: 'application/json' }
    if (body !== undefined) headers['Content-Type'] = 'application/json'
    if (token) headers.Authorization = `Bearer ${token}`
    let res
    try {
      res = await globalThis.fetch(`${base}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
    } catch (e) {
      throw new ExitError(1, `${method} ${path}: сеть недоступна (${e.message}). Поднят ли стек? stack.sh up`)
    }
    const text = await res.text()
    const ok = expected ? expected.includes(res.status) : res.status >= 200 && res.status < 300
    if (!ok) {
      const err = new ExitError(5, `${method} ${path} -> ${res.status}\n${text}`)
      err.status = res.status
      err.body = text
      throw err
    }
    if (!text) return null
    try {
      return JSON.parse(text)
    } catch {
      return text
    }
  }
}

async function waitReady(call) {
  const deadline = Date.now() + 120_000
  for (;;) {
    try {
      await call('GET', '/api/health/ready', { expected: [200] })
      return
    } catch (e) {
      if (Date.now() > deadline) throw new ExitError(1, `API не стал готов за 120 с: ${e.message}`)
      await new Promise((r) => setTimeout(r, 2000))
    }
  }
}

const selfCheckError = (msg) => new ExitError(5, `Самопроверка: ${msg}`)

function findProduct(storefront, name) {
  for (const c of storefront.categories) {
    const p = c.products.find((x) => x.name === name)
    if (p) return p
  }
  throw new ExitError(5, `В витрине нет товара «${name}»`)
}

const isoNoMs = (d) => d.toISOString().replace(/\.\d+Z$/, 'Z')

// ---------- основной сценарий ----------

export async function main(env = process.env) {
  const base = checkApiUrl(env.SHOTS_API_URL || 'http://localhost:55000', env.SHOTS_ALLOW_HOST || '')
  const call = makeClient(base)

  await waitReady(call)
  // Swagger UI и swagger.json включаются одним условием IsDevelopment (ApiExtensions.UseServiceBookingSwagger).
  // Сам swagger.json проверять нельзя: в dev он отвечает 500 (коллизия schemaId WorkingHoursDto), поэтому берём UI.
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

  if (city.timeZoneId) assertShootingWindow(city.timeZoneId)

  const legal = await call('GET', '/api/legal/documents')
  const version = (type) => {
    const d = legal.documents.find((x) => x.type === type)
    if (!d) throw new ExitError(5, `В /api/legal/documents нет документа ${type}`)
    return d.version
  }
  const privacy = version('Privacy')
  const termsClient = version('TermsClient')
  const termsOwner = version('TermsOwner')

  const password = makePassword()
  let auth
  try {
    auth = await call('POST', '/api/auth/register', {
      body: {
        firstName: OWNER.firstName, lastName: OWNER.lastName, phone: OWNER.phone, password, email: null,
        legal: { privacyAcknowledgedVersion: privacy, termsAcceptedVersion: termsClient },
      },
    })
  } catch (e) {
    if (e.code === 5 && e.status === 400 && /Duplicate|уже|занят|taken/i.test(e.body || '')) {
      throw new ExitError(2, 'База уже засеяна — выполните stack.sh reset.')
    }
    throw e
  }
  if (!auth?.token) auth = await call('POST', '/api/auth/login', { body: { phone: OWNER.phone, password } })
  let token = auth.token

  const created = await call('POST', '/api/shops', {
    token,
    body: {
      name: SHOP.name, slug: SHOP.slug, cityId: city.id, address: SHOP.address, phone: SHOP.phone,
      description: SHOP.description, ownerTerms: { version: termsOwner },
    },
  })
  if (created.token) token = created.token
  const shopId = created.shop.id
  const timeZoneId = created.shop.timeZoneId || city.timeZoneId
  assertShootingWindow(timeZoneId)
  const shopPath = `/api/shops/${shopId}`

  await call('PUT', `${shopPath}/settings`, {
    token, body: { customerMode: 'Anyone', acceptanceMode: 'Manual', allowCustomerCancel: true, trackStock: false },
  })
  const days = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
  await call('PUT', `${shopPath}/working-hours`, {
    token, body: { days: days.map((dayOfWeek) => ({ dayOfWeek, intervals: [{ start: '07:00', end: '23:00' }] })) },
  })
  await call('PUT', `${shopPath}/pickup-settings`, {
    token, body: { asapEnabled: true, scheduledEnabled: true, slotStepMinutes: 15, preorderDays: 1, minPrepMinutes: 60 },
  })
  await call('PUT', `${shopPath}/acceptance`, { token, body: { mode: 'Accepting' } })

  const notif = await call('GET', `${shopPath}/notification-settings`, { token })
  if (notif.customerMessengerEnabled) {
    await call('PUT', `${shopPath}/notification-settings`, {
      token,
      body: {
        staffPushEnabled: notif.staffPushEnabled, customerWebPushEnabled: notif.customerWebPushEnabled,
        customerMessengerEnabled: false, deliveryMode: notif.deliveryMode, priorityTransport: notif.priorityTransport,
      },
    })
  }

  for (const cat of CATEGORIES) {
    const c = await call('POST', `${shopPath}/categories`, { token, body: { name: cat.name } })
    for (const p of cat.products) {
      const body = { categoryId: c.id, name: p.name, unit: p.unit, price: p.price, isPublished: true }
      if (p.unit === 'Piece') body.portionText = p.portionText
      else {
        body.weightStepGrams = p.weightStepGrams
        body.minQuantityGrams = p.minQuantityGrams
      }
      await call('POST', `${shopPath}/products`, { token, body })
    }
  }

  const now = new Date()

  const storefront = await call('GET', `/api/storefront/${SHOP.slug}`)
  const today = dateInZone(now, timeZoneId)
  const slots = await call('GET', `/api/storefront/${SHOP.slug}/pickup-slots?date=${today}`)

  const placed = {}
  for (const key of ORDER_SEQUENCE) {
    const spec = ORDERS[key]
    const items = spec.lines.map(([name, quantity]) => {
      const p = findProduct(storefront, name)
      return { productId: p.id, quantity, expectedUnitPrice: p.price }
    })
    let pickup = null
    if (spec.scheduleMinutes != null) {
      const threshold = now.getTime() + spec.scheduleMinutes * 60_000
      const slot = slots.slots.find((s) => new Date(s.startUtc).getTime() >= threshold)
      if (!slot) throw new ExitError(4, `Нет слота не раньше «сейчас + ${spec.scheduleMinutes} мин» на ${today}.`)
      pickup = { kind: 'Slot', date: today, slotStartUtc: slot.startUtc }
    }
    const res = await call('POST', `/api/storefront/${SHOP.slug}/orders`, {
      body: {
        idempotencyKey: randomUUID(), items, customerName: spec.name, customerPhone: spec.phone,
        comment: spec.comment, captchaToken: null, pickup, notifyByMessenger: false,
      },
    })
    placed[key] = res.order
  }

  const boardOf = () => call('GET', `${shopPath}/order-board`, { token })
  const cardOf = async (key) => {
    const board = await boardOf()
    const all = [...(board.newOrders ?? []), ...(board.accepted ?? []), ...(board.ready ?? [])]
    const card = all.find((o) => o.number === placed[key].number && o.customerName === ORDERS[key].name)
    if (!card) throw new ExitError(5, `Заказ ${key} (№${placed[key].number}) не найден на доске`)
    return card
  }
  for (const key of ACCEPT_SEQUENCE) {
    const card = await cardOf(key)
    await call('POST', `${shopPath}/orders/${card.id}/accept`, { token, body: { expectedVersion: card.version } })
  }
  for (const key of READY_SEQUENCE) {
    const card = await cardOf(key)
    await call('POST', `${shopPath}/orders/${card.id}/ready`, { token, body: { expectedVersion: card.version } })
  }

  // ---------- самопроверка ----------
  const board = await boardOf()
  const counts = { newOrders: (board.newOrders ?? []).length, accepted: (board.accepted ?? []).length, ready: (board.ready ?? []).length }
  if (counts.newOrders !== 2 || counts.accepted !== 2 || counts.ready !== 2) {
    throw selfCheckError(`на доске New=${counts.newOrders}, Accepted=${counts.accepted}, Ready=${counts.ready}, ожидалось 2/2/2`)
  }
  if ((board.preorders ?? []).length !== 0) throw selfCheckError('на доске есть предзаказы, ожидалось ни одного')

  const orderC = placed[ORDER_PAGE_KEY]
  const pub = await call('GET', `/api/orders/public/${orderC.token}`)
  if (pub.status !== 'Accepted') throw selfCheckError(`заказ C имеет статус ${pub.status}, ожидался Accepted`)
  if (pub.pickup?.kind !== 'Slot') throw selfCheckError(`у заказа C pickup.kind = ${pub.pickup?.kind}, ожидался Slot`)
  if (pub.pickup.date !== today) throw selfCheckError(`дата получения заказа C ${pub.pickup.date}, ожидалась сегодняшняя ${today}`)
  const pickupClock = clockInZone(new Date(pub.pickup.startUtc), timeZoneId).text

  const active = [...board.newOrders, ...board.accepted, ...board.ready]
  const earliestDue = Math.min(...active.map((o) => new Date(o.pickup.dueUtc).getTime()))

  const state = {
    schemaVersion: 1,
    seededAtUtc: isoNoMs(new Date()),
    apiUrl: base,
    shop: { id: shopId, slug: SHOP.slug, name: SHOP.name, timeZoneId },
    owner: { phone: OWNER.phone, password },
    orderPage: { token: orderC.token, number: pub.number, status: 'Accepted', statusText: pub.statusText, pickupClock },
    board: { ...counts, earliestDueUtc: isoNoMs(new Date(earliestDue)) },
  }
  const stateDir = join(dirname(fileURLToPath(import.meta.url)), '.state')
  mkdirSync(stateDir, { recursive: true })
  const stateFile = join(stateDir, 'seed.json')
  writeFileSync(stateFile, `${JSON.stringify(state, null, 2)}\n`, { mode: 0o600 })

  out(`Засев готов: магазин «${SHOP.name}», заказов 6 (New 2, Accepted 2, Ready 2).`)
  out(`Состояние записано: ${stateFile}`)
  out('Дальше: npm run shots:capture')
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
        process.stderr.write(`[seed] ${e.message}\n`)
        process.exit(e.code)
      }
      process.stderr.write(`[seed] Непредвиденная ошибка: ${e?.stack ?? e}\n`)
      process.exit(1)
    },
  )
}
