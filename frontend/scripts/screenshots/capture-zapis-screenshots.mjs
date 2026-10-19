#!/usr/bin/env node
/* global document, window, NodeFilter, location */
// Съёмка скриншотов для главной «Записи» (ezbook.ru). Тот же подход, что у goods (capture-goods-screenshots.mjs):
// читает .state/zapis-seed.json (seed-zapis-demo.mjs), пишет 5 WebP и screenshots.json в frontend/src/assets/screenshots/zapis/.
// Кадры: шаг «Выберите время» записи клиента (телефон) и «Мои записи» мастера-владельца (десктоп и телефон).
// Коды выхода: 0 успех, 1 непредвиденная ошибка, 2 нет/устарел zapis-seed.json, 4 запретная строка, 5 бюджет веса или размер.
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import { execSync } from 'node:child_process'

const HELP = `Использование: node scripts/screenshots/capture-zapis-screenshots.mjs [--help]   (или npm run shots:zapis:capture)

Переменные окружения:
  SHOTS_WEB_URL      адрес уже запущенного фронта ezbook (иначе поднимается Vite на порту 55173)
  SHOTS_CHROME_PATH  путь к Chrome (иначе установленный Google Chrome, channel: 'chrome')
  SHOTS_FORBID_EXTRA строки через "|", добавляются к списку запретных (для проверки кода 4)

Коды выхода: 0 успех, 1 ошибка, 2 нет zapis-seed.json или данные устарели, 4 запретная строка, 5 бюджет/размер.`
if (process.argv.includes('--help') || process.argv.includes('-h')) {
  console.log(HELP)
  process.exit(0)
}

const here = dirname(fileURLToPath(import.meta.url))
const frontendRoot = join(here, '..', '..')
const outDir = join(frontendRoot, 'src', 'assets', 'screenshots', 'zapis')
const statePath = join(here, '.state', 'zapis-seed.json')
const KB = 1024
// Колонка «Моих записей» узкая (max-w-3xl): на 1280 по бокам пусто, а на главной кадр сжимается вдвое.
const DESKTOP_WIDTH = 1024
// Две записи дня: на главной десктопный кадр идёт во всю ширину секции, с четырьмя он выходит выше экрана.
const BOOKING_ROWS = 2
const FILES = [...['services', 'master', 'date', 'slots', 'contacts', 'done'].map((k) => `booking-${k}-2x`), 'bookings-desktop-1x', 'bookings-desktop-2x', 'bookings-phone-2x']

class Exit extends Error {
  constructor(code, message) {
    super(message)
    this.code = code
  }
}

const FORBIDDEN = [
  'localhost', '127.0.0.1', 'Загрузка…', 'Не удалось', 'Нет доступных слотов', 'Записей на ближайшую неделю нет',
  'пробн', 'Подтвердите телефон', 'пропущено',
  ...(process.env.SHOTS_FORBID_EXTRA ? process.env.SHOTS_FORBID_EXTRA.split('|').filter(Boolean) : []),
]

/** Размер в пикселях по заголовку WebP (VP8 / VP8L / VP8X). */
function webpSize(buf) {
  const kind = buf.toString('ascii', 12, 16)
  if (kind === 'VP8 ') return { w: buf.readUInt16LE(26) & 0x3fff, h: buf.readUInt16LE(28) & 0x3fff }
  if (kind === 'VP8L') {
    const b = buf.readUInt32LE(21)
    return { w: (b & 0x3fff) + 1, h: ((b >> 14) & 0x3fff) + 1 }
  }
  if (kind === 'VP8X') return { w: buf.readUIntLE(24, 3) + 1, h: buf.readUIntLE(27, 3) + 1 }
  throw new Error(`Неизвестный тип WebP-чанка: ${kind}`)
}

function loadState() {
  if (!existsSync(statePath)) throw new Exit(2, `Нет ${statePath}. Выполните stack.sh reset и npm run shots:zapis:seed.`)
  const state = JSON.parse(readFileSync(statePath, 'utf8'))
  if (state.schemaVersion !== 1) throw new Exit(2, `Неизвестная версия zapis-seed.json: ${state.schemaVersion}. Пересейте данные.`)
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: state.company.timeZoneId }).format(new Date())
  if (today >= state.shootDate) throw new Exit(2, `День съёмки ${state.shootDate} уже наступил: «Завтра» в кадре не получится. Пересейте данные.`)
  return state
}

/** Vite: пока кадров ещё нет, импорты .webp/.json из shots.ts не должны ронять весь фронт. */
function missingShotsPlugin() {
  const prefix = '\0zapis-shots-stub:'
  const dirNorm = outDir.replaceAll('\\', '/')
  return {
    name: 'zapis-shots-missing-stub',
    enforce: 'pre',
    resolveId(id, importer) {
      if (!importer || !/\.(webp|json)$/.test(id) || !id.startsWith('./')) return null
      const full = join(dirname(importer), id).replaceAll('\\', '/')
      if (!full.startsWith(dirNorm) || existsSync(full)) return null
      return `${prefix}${id}.js`
    },
    load(id) {
      if (!id.startsWith(prefix)) return null
      if (id.endsWith('.webp.js')) return 'export default ""'
      return `export default ${JSON.stringify({
        bookingFlow: { serviceName: '', masterName: '', slides: [] },
        bookingsDesktop: { cssWidth: 1024, cssHeight: 800 },
        bookingsPhone: { cssWidth: 390, cssHeight: 800 },
      })}`
    },
  }
}

async function startWeb(state) {
  if (process.env.SHOTS_WEB_URL) return { url: process.env.SHOTS_WEB_URL.replace(/\/$/, ''), close: async () => {} }
  process.env.VITE_API_TARGET = state.apiUrl
  const { createServer } = await import('vite')
  const server = await createServer({
    configFile: join(frontendRoot, 'vite.config.ts'),
    plugins: [missingShotsPlugin()],
    server: { port: 55173, strictPort: true },
    logLevel: 'warn',
  })
  await server.listen()
  return { url: 'http://localhost:55173', close: () => server.close() }
}

async function launch() {
  const { chromium } = await import('playwright-core')
  const args = ['--hide-scrollbars']
  try {
    if (process.env.SHOTS_CHROME_PATH) return await chromium.launch({ executablePath: process.env.SHOTS_CHROME_PATH, headless: true, args })
    return await chromium.launch({ channel: 'chrome', headless: true, args })
  } catch (e) {
    throw new Error(`Не удалось запустить Chrome (${e.message.split('\n')[0]}). Установите Google Chrome или задайте SHOTS_CHROME_PATH.`)
  }
}

const newContext = (browser, state, width, height, dpr) =>
  browser.newContext({
    viewport: { width, height },
    deviceScaleFactor: dpr,
    locale: 'ru-RU',
    timezoneId: state.company.timeZoneId,
    colorScheme: 'light',
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    permissions: [],
  })

async function settle(page) {
  await page.evaluate(() => document.fonts.ready)
  await page.waitForLoadState('networkidle', { timeout: 5000 }).catch(() => {})
  await page.waitForTimeout(400)
}

/** Текст, видимый внутри прямоугольника кадра (CSS px от верха страницы). */
async function visibleTextIn(page, clip) {
  return page.evaluate((c) => {
    const out = []
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT)
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      const el = n.parentElement
      if (!el || ['SCRIPT', 'STYLE', 'NOSCRIPT'].includes(el.tagName)) continue
      const r = document.createRange()
      r.selectNodeContents(n)
      const rect = r.getBoundingClientRect()
      if (rect.width === 0 || rect.height === 0) continue
      const top = rect.top + window.scrollY
      const left = rect.left + window.scrollX
      if (top < c.y + c.height && top + rect.height > c.y && left < c.x + c.width && left + rect.width > c.x) out.push(n.textContent)
    }
    return out.join(' ').replace(/\s+/g, ' ')
  }, clip)
}

async function checkForbidden(page, name, clip) {
  const text = await visibleTextIn(page, clip)
  for (const s of FORBIDDEN) {
    if (text.toLowerCase().includes(s.toLowerCase())) throw new Exit(4, `В кадре «${name}» найдена запретная строка «${s}».`)
  }
  return text
}

/** Кодирует WebP через CDP с понижением качества до бюджета. */
async function encode(page, { name, clip, dpr, budget }) {
  const cdp = await page.context().newCDPSession(page)
  const start = dpr === 2 ? 82 : 85
  try {
    for (let q = start; q >= 60; q -= 5) {
      const { data } = await cdp.send('Page.captureScreenshot', {
        format: 'webp',
        quality: q,
        clip: { ...clip, scale: dpr },
        captureBeyondViewport: true,
      })
      const buf = Buffer.from(data, 'base64')
      if (buf.length <= budget) {
        const size = webpSize(buf)
        if (size.w !== clip.width * dpr || size.h !== clip.height * dpr) {
          throw new Exit(5, `${name}: размер ${size.w}x${size.h}, ожидалось ${clip.width * dpr}x${clip.height * dpr}.`)
        }
        return buf
      }
    }
  } finally {
    await cdp.detach()
  }
  throw new Exit(5, `${name}: не уложились в бюджет ${Math.round(budget / KB)} КБ даже при качестве 60. Уменьшите высоту обрезки, бюджет не поднимать.`)
}

const dayAria = (ymd) =>
  new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'long', timeZone: 'UTC' }).format(new Date(`${ymd}T12:00:00Z`))

// Карусель клиента: весь путь записи, по кадру на шаг окна записи. Все кадры одного размера: окно записи растянуто до своей
// предельной высоты (88vh от экрана 720 = 634 px), шаг — сверху, как на экране телефона. Длинные шаги (услуги, контакты)
// снимаются как на телефоне — верхняя часть окна, остальное прокручивается.
const FLOW_VIEWPORT = { width: 390, height: 720 }
// На шаге времени — только первые четыре ряда слотов, чтобы кадр не вытягивался.
const SLOT_ROWS = 4

/** Прямоугольник окна записи с полем вокруг; фон под окном — ровный кремовый вместо размытой страницы. */
async function panelClip(page) {
  await page.mouse.move(0, 0) // курсор после клика остаётся над кнопкой: без этого она снимется в ховере
  await settle(page)
  const box = await page.evaluate(() => {
    const panel = document.querySelector('.fixed.inset-0 > div')
    if (!panel) return null
    panel.scrollTop = 0
    const r = panel.getBoundingClientRect()
    return { x: r.left, y: r.top + window.scrollY, width: r.width, height: r.height }
  })
  if (!box) throw new Exit(5, 'Окно записи не найдено.')
  const pad = 14
  const x = Math.max(0, Math.floor(box.x - pad))
  const y = Math.max(0, Math.floor(box.y - pad))
  return { x, y, width: FLOW_VIEWPORT.width - 2 * x, height: Math.ceil(box.height + 2 * pad) }
}

/**
 * Проходит запись гостя от услуги до подтверждения и снимает каждый шаг (2x). Последний шаг создаёт настоящую запись
 * на самое позднее свободное время дня кадра — первые ряды слотов, которые попадают в кадр, она не трогает.
 */
async function shootFlow(browser, state, webUrl, tmp) {
  const ctx = await newContext(browser, state, FLOW_VIEWPORT.width, FLOW_VIEWPORT.height, 2)
  const page = await ctx.newPage()
  const slides = []
  const shoot = async (key, mustContain) => {
    const clip = await panelClip(page)
    const text = await checkForbidden(page, `booking-${key}`, clip)
    for (const s of mustContain) if (!text.includes(s)) throw new Exit(5, `В кадре «${key}» нет «${s}».`)
    const buf = await encode(page, { name: `booking-${key}-2x`, clip, dpr: 2, budget: 90 * KB })
    writeFileSync(join(tmp, `booking-${key}-2x.webp`), buf)
    slides.push({ key, cssWidth: clip.width, cssHeight: clip.height, bytes: buf.length })
  }

  await page.goto(`${webUrl}/company/${state.company.slug}`)
  await page.locator('h3', { hasText: state.clientShot.serviceName }).first().waitFor()
  await page.addStyleTag({
    content: `.fixed.inset-0{background:#F6F1EA!important;backdrop-filter:none!important}
      .fixed.inset-0>div{height:88vh!important}
      .grid.grid-cols-3>button:nth-child(n+${SLOT_ROWS * 3 + 1}){display:none!important}`,
  })
  await page.getByRole('button', { name: 'Записаться' }).first().click()
  await page.getByRole('button', { name: 'Продолжить' }).waitFor()
  await shoot('services', [state.clientShot.serviceName, 'Добавить услугу'])

  await page.getByRole('button', { name: 'Продолжить' }).click()
  await page.getByRole('heading', { name: 'Выберите мастера' }).waitFor()
  await shoot('master', state.clientShot.masterNames)

  await page.getByRole('button', { name: new RegExp(state.clientShot.masterName) }).click()
  const day = page.getByRole('button', { name: dayAria(state.clientDate), exact: true })
  await day.waitFor()
  await shoot('date', ['Выберите дату'])

  await day.click()
  await page.getByRole('heading', { name: 'Выберите время' }).waitFor()
  await page.waitForFunction(() => document.querySelectorAll('.animate-pulse').length === 0)
  const slotButtons = page.locator('.grid.grid-cols-3 > button')
  const slotCount = await slotButtons.count()
  if (slotCount < SLOT_ROWS * 3 + 1) throw new Exit(5, `Свободных слотов ${slotCount}, нужно больше ${SLOT_ROWS * 3}.`)
  await shoot('slots', ['Выберите время'])

  // Последний слот дня скрыт стилем выше — нажимаем его из скрипта страницы.
  const lastSlot = (await slotButtons.last().textContent())?.trim() ?? ''
  await slotButtons.last().evaluate((el) => el.click())
  await page.getByPlaceholder('Иван Иванов').fill(state.clientGuest.name)
  await page.getByPlaceholder('+7 (900) 000-00-00').fill(state.clientGuest.phone)
  await page.evaluate(() => document.activeElement?.blur()) // без курсора в поле телефона
  await shoot('contacts', [state.clientShot.serviceName, lastSlot])

  await page.getByRole('button', { name: 'Подтвердить запись' }).click()
  await page.getByText('Запись подтверждена').waitFor({ timeout: 10000 })
  await shoot('done', ['Запись подтверждена', lastSlot])

  await ctx.close()
  const sizes = new Set(slides.map((sl) => `${sl.cssWidth}x${sl.cssHeight}`))
  if (sizes.size !== 1) throw new Exit(5, `Кадры карусели разного размера: ${[...sizes].join(', ')}.`)
  return slides
}

async function signIn(page, webUrl, state) {
  await page.goto(`${webUrl}/login`)
  try {
    await page.getByLabel('Телефон').fill(state.owner.phone)
    await page.getByLabel('Пароль').fill(state.owner.password)
    await page.getByRole('button', { name: 'Войти' }).click()
    await page.waitForFunction(() => !location.pathname.startsWith('/login'), null, { timeout: 8000 })
  } catch {
    const res = await page.evaluate(async ({ phone, password }) => {
      const r = await fetch('/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ phone, password }) })
      return r.ok ? r.json() : null
    }, { phone: state.owner.phone, password: state.owner.password })
    if (!res) throw new Error('Не удалось войти владельцем ни через форму, ни через API.')
    await page.evaluate((r) => {
      localStorage.setItem('auth-store', JSON.stringify({ state: { user: { id: r.userId, phone: r.phone, email: r.email, firstName: r.firstName, lastName: r.lastName, roles: r.roles }, token: r.token }, version: 0 }))
    }, res)
  }
}

/**
 * «Мои записи»: кадр от верха страницы до низа `rows`-й записи первого дня (завтра).
 * Метка доставки напоминания убирается из кадра: на локальном стенде у салона нет каналов уведомлений, и сервер честно
 * пишет «напоминание пропущено». У салона с подключённым каналом там «ожидает отправки» — к тому, что показывает кадр, это
 * не относится, а в рекламе вводит в заблуждение. Больше в странице ничего не меняется.
 */
async function openBookings(page, webUrl, state, width, rows) {
  await page.goto(`${webUrl}/my-bookings`)
  await page.getByRole('heading', { name: 'Мои записи' }).waitFor()
  await page.getByRole('heading', { name: 'Завтра' }).waitFor()
  await settle(page)
  const removed = await page.evaluate(() => {
    const chips = [...document.querySelectorAll('span')].filter((el) => /^\s*напоминание /.test(el.textContent ?? '') && el.querySelector('svg'))
    chips.forEach((el) => el.remove())
    return chips.length
  })
  // Не меньше, чем записей владелицы из засева: съёмка карусели добавляет ещё одну запись на послезавтра.
  if (removed < state.ownerBookingCount) throw new Exit(5, `Меток напоминания ${removed}, ожидалось не меньше ${state.ownerBookingCount}: разметка «Мои записи» изменилась, проверьте скрипт.`)
  const bottom = await page.evaluate((n) => {
    const h = [...document.querySelectorAll('h3')].find((x) => x.textContent === 'Завтра')
    const list = h?.nextElementSibling
    if (!list) return null
    const last = Math.min(n, list.children.length)
    const row = list.children[last - 1]
    // Под последней записью дня — поле страницы; если ниже есть ещё записи, режем по зазору, чтобы не торчал край следующей.
    const pad = last < list.children.length ? 4 : 28
    return Math.ceil(row.getBoundingClientRect().bottom + window.scrollY) + pad
  }, rows)
  if (!bottom) throw new Exit(5, 'Группа «Завтра» не найдена.')
  return { x: 0, y: 0, width, height: Math.min(bottom, 1300) }
}

async function shootBookings(browser, state, webUrl, tmp) {
  const desk = {}
  let deskClip = null
  for (const dpr of [1, 2]) {
    const ctx = await newContext(browser, state, DESKTOP_WIDTH, 900, dpr)
    const page = await ctx.newPage()
    await signIn(page, webUrl, state)
    deskClip = await openBookings(page, webUrl, state, DESKTOP_WIDTH, BOOKING_ROWS)
    const text = await checkForbidden(page, 'bookings-desktop', deskClip)
    for (const n of state.ownerClientNames.slice(0, BOOKING_ROWS)) if (!text.includes(n)) throw new Exit(5, `В кадре «Мои записи» нет клиента «${n}».`)
    desk[`${dpr}x`] = await encode(page, { name: `bookings-desktop-${dpr}x`, clip: deskClip, dpr, budget: 200 * KB })
    await ctx.close()
  }
  for (const k of ['1x', '2x']) writeFileSync(join(tmp, `bookings-desktop-${k}.webp`), desk[k])

  const ctx = await newContext(browser, state, 390, 844, 2)
  const page = await ctx.newPage()
  await signIn(page, webUrl, state)
  const phoneClip = await openBookings(page, webUrl, state, 390, BOOKING_ROWS)
  await checkForbidden(page, 'bookings-phone', phoneClip)
  const phone = await encode(page, { name: 'bookings-phone-2x', clip: phoneClip, dpr: 2, budget: 140 * KB })
  writeFileSync(join(tmp, 'bookings-phone-2x.webp'), phone)
  const browserVersion = browser.version()
  await ctx.close()
  return {
    desktop: { cssHeight: deskClip.height, bytes: { '1x': desk['1x'].length, '2x': desk['2x'].length } },
    phone: { cssHeight: phoneClip.height, bytes: { '2x': phone.length } },
    browserVersion,
  }
}

function sourceCommit() {
  const rev = execSync('git rev-parse --short HEAD', { cwd: frontendRoot }).toString().trim()
  const dirty = execSync('git status --porcelain -- . ":(exclude)src/assets/screenshots/zapis"', { cwd: frontendRoot }).toString().trim() !== ''
  return dirty ? `${rev}-dirty` : rev
}

async function main() {
  const state = loadState()
  const tmp = mkdtempSync(join(tmpdir(), 'zapis-shots-'))
  const web = await startWeb(state)
  let browser
  try {
    browser = await launch()
    const flow = await shootFlow(browser, state, web.url, tmp)
    const lists = await shootBookings(browser, state, web.url, tmp)
    const manifest = {
      schemaVersion: 1,
      capturedAt: new Date().toISOString().replace(/\.\d+Z$/, 'Z'),
      sourceCommit: sourceCommit(),
      browser: lists.browserVersion.startsWith('Chrome') ? lists.browserVersion : `Chrome/${lists.browserVersion}`,
      bookingFlow: {
        serviceName: state.clientShot.serviceName,
        masterName: state.clientShot.masterName,
        slides: flow.map((f) => ({ key: f.key, file: `booking-${f.key}-2x.webp`, cssWidth: f.cssWidth, cssHeight: f.cssHeight, bytes: f.bytes })),
      },
      bookingsDesktop: {
        cssWidth: DESKTOP_WIDTH,
        cssHeight: lists.desktop.cssHeight,
        files: { '1x': 'bookings-desktop-1x.webp', '2x': 'bookings-desktop-2x.webp' },
        bytes: lists.desktop.bytes,
        bookingCount: state.ownerBookingCount,
      },
      bookingsPhone: {
        cssWidth: 390,
        cssHeight: lists.phone.cssHeight,
        files: { '2x': 'bookings-phone-2x.webp' },
        bytes: lists.phone.bytes,
      },
    }
    mkdirSync(outDir, { recursive: true })
    for (const f of FILES) writeFileSync(join(outDir, `${f}.webp`), readFileSync(join(tmp, `${f}.webp`)))
    writeFileSync(join(outDir, 'screenshots.json'), JSON.stringify(manifest, null, 2) + '\n')
    console.table(FILES.map((f) => ({ file: f, KB: (readFileSync(join(outDir, `${f}.webp`)).length / KB).toFixed(1) })))
    console.log(`Манифест: ${join(outDir, 'screenshots.json')}`)
  } finally {
    if (browser) await browser.close()
    await web.close()
    rmSync(tmp, { recursive: true, force: true })
  }
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error(`capture-zapis: ${e.message}`)
    process.exit(e instanceof Exit ? e.code : 1)
  },
)
