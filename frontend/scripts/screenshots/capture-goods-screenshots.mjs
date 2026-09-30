#!/usr/bin/env node
/* global document, window, NodeFilter, location */
// Съёмка скриншотов для главной goods (ARCHITECTURE_CYCLE30.md §30.8). Читает .state/seed.json (§30.22),
// пишет 5 WebP и screenshots.json (§30.23) в frontend/goods/src/assets/screenshots/.
// Коды выхода: 0 успех, 1 непредвиденная ошибка, 2 нет/устарел seed.json, 4 запретная строка или модалка,
// 5 бюджет веса или размер в пикселях не сошлись.
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import { execSync } from 'node:child_process'

const HELP = `Использование: node scripts/screenshots/capture-goods-screenshots.mjs [--help]

Переменные окружения:
  SHOTS_WEB_URL      адрес уже запущенного goods-фронта (иначе поднимается Vite на порту 55174)
  SHOTS_CHROME_PATH  путь к Chrome (иначе установленный Google Chrome, channel: 'chrome')
  SHOTS_FORBID_EXTRA строки через "|", добавляются к списку запретных (для проверки кода 4)

Коды выхода: 0 успех, 1 ошибка, 2 нет seed.json или данные устарели, 4 запретная строка/модалка, 5 бюджет/размер.`
if (process.argv.includes('--help') || process.argv.includes('-h')) {
  console.log(HELP)
  process.exit(0)
}

const here = dirname(fileURLToPath(import.meta.url))
const frontendRoot = join(here, '..', '..')
const outDir = join(frontendRoot, 'goods', 'src', 'assets', 'screenshots')
const statePath = join(here, '.state', 'seed.json')
const KB = 1024

class Exit extends Error {
  constructor(code, message) {
    super(message)
    this.code = code
  }
}

const FORBIDDEN = [
  'Просрочен', 'Нет связи', 'Звук выключен', 'Звук заблокирован', 'Сообщение покупателю', 'MAX/WhatsApp',
  'localhost', '127.0.0.1', 'Ссылка на этот заказ', 'Отключите автоблокировку', 'Загрузка…', 'Не удалось',
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
  if (!existsSync(statePath)) throw new Exit(2, `Нет ${statePath}. Выполните stack.sh reset и npm run shots:seed.`)
  const state = JSON.parse(readFileSync(statePath, 'utf8'))
  if (state.schemaVersion !== 1) throw new Exit(2, `Неизвестная версия seed.json: ${state.schemaVersion}. Пересейте данные.`)
  const due = Date.parse(state.board.earliestDueUtc)
  if (Date.now() > due - 5 * 60_000) throw new Exit(2, 'Данные устарели (до ближайшего срока заказа меньше 5 минут). Пересейте: stack.sh reset и npm run shots:seed.')
  return state
}

/** Vite: пока кадров ещё нет, импорты .webp/.json из shots.ts не должны ронять весь фронт. */
function missingShotsPlugin() {
  const prefix = '\0shots-stub:'
  const dirNorm = outDir.replaceAll('\\', '/')
  return {
    name: 'shots-missing-stub',
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
        orderPage: { cssWidth: 390, cssHeight: 600, orderNumber: 1, pickupClock: '00:00' },
        boardDesktop: { cssWidth: 1280, cssHeight: 800 },
        boardPhone: { cssWidth: 390, cssHeight: 800 },
      })}`
    },
  }
}

async function startWeb(state) {
  if (process.env.SHOTS_WEB_URL) return { url: process.env.SHOTS_WEB_URL.replace(/\/$/, ''), close: async () => {} }
  process.env.VITE_API_TARGET = state.apiUrl
  const { createServer } = await import('vite')
  const server = await createServer({
    configFile: join(frontendRoot, 'vite.goods.config.ts'),
    plugins: [missingShotsPlugin()],
    server: { port: 55174, strictPort: true },
    logLevel: 'warn',
  })
  await server.listen()
  return { url: 'http://localhost:55174', close: () => server.close() }
}

async function launch() {
  const { chromium } = await import('playwright-core')
  const args = ['--autoplay-policy=no-user-gesture-required', '--hide-scrollbars']
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
    timezoneId: state.shop.timeZoneId,
    colorScheme: 'light',
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    permissions: [],
  })

async function settle(page) {
  await page.evaluate(() => document.fonts.ready)
  await page.waitForLoadState('networkidle', { timeout: 5000 }).catch(() => {}) // доска опрашивает API, тишины может не быть
  if ((await page.locator('[role="dialog"]').count()) > 0) throw new Exit(4, 'На странице открыто модальное окно ([role="dialog"]).')
  await page.waitForTimeout(400)
}

/** Текст, видимый выше линии обрезки (низ кадра в CSS px от верха страницы). */
async function visibleTextAbove(page, bottom) {
  return page.evaluate((limit) => {
    const out = []
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT)
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      const el = n.parentElement
      if (!el || ['SCRIPT', 'STYLE', 'NOSCRIPT'].includes(el.tagName)) continue
      const r = document.createRange()
      r.selectNodeContents(n)
      const rect = r.getBoundingClientRect()
      if (rect.width === 0 || rect.height === 0) continue
      if (rect.top + window.scrollY < limit) out.push(n.textContent)
    }
    return out.join(' ')
  }, bottom)
}

async function checkForbidden(page, name, bottom) {
  const text = await visibleTextAbove(page, bottom)
  for (const s of FORBIDDEN) {
    if (text.includes(s)) throw new Exit(4, `В кадре «${name}» найдена запретная строка «${s}».`)
  }
}

/** Низ элемента в CSS px от верха документа. */
const bottomOf = (page, selector) =>
  page.evaluate((sel) => {
    const el = document.querySelector(sel)
    if (!el) return null
    const r = el.getBoundingClientRect()
    return Math.ceil(r.bottom + window.scrollY)
  }, selector)

/** Кодирует WebP через CDP с понижением качества до бюджета (§30.8 п. 9). */
async function encode(page, { name, width, height, dpr, budget }) {
  const cdp = await page.context().newCDPSession(page)
  const start = dpr === 2 ? 82 : 85
  try {
    for (let q = start; q >= 60; q -= 5) {
      const { data } = await cdp.send('Page.captureScreenshot', {
        format: 'webp',
        quality: q,
        clip: { x: 0, y: 0, width, height, scale: dpr },
        captureBeyondViewport: true,
      })
      const buf = Buffer.from(data, 'base64')
      if (buf.length <= budget) {
        const size = webpSize(buf)
        if (size.w !== width * dpr || size.h !== height * dpr) {
          throw new Exit(5, `${name}: размер ${size.w}x${size.h}, ожидалось ${width * dpr}x${height * dpr}.`)
        }
        return { buf, quality: q }
      }
    }
  } finally {
    await cdp.detach()
  }
  throw new Exit(5, `${name}: не уложились в бюджет ${Math.round(budget / KB)} КБ даже при качестве 60. Уменьшите высоту обрезки, бюджет не поднимать.`)
}

async function shootOrder(browser, state, webUrl, tmp) {
  const result = {}
  let height = 0
  let meta = null
  for (const dpr of [1, 2]) {
    const ctx = await newContext(browser, state, 390, 844, dpr)
    const page = await ctx.newPage()
    await page.goto(`${webUrl}/o/${state.orderPage.token}`)
    await page.waitForSelector('[data-testid="order-number"]')
    await settle(page)
    const number = (await page.textContent('[data-testid="order-number"]')) ?? ''
    if (!new RegExp(`(^|\\D)${state.orderPage.number}(\\D|$)`).test(number)) throw new Exit(5, `Номер заказа на странице «${number.trim()}», в seed.json ${state.orderPage.number}.`)
    const pickup = (await page.textContent('[data-testid="order-pickup"]')) ?? ''
    if (!pickup.includes(state.orderPage.pickupClock)) throw new Exit(5, `Время получения «${pickup.trim()}» не содержит ${state.orderPage.pickupClock}.`)
    const bottom = await bottomOf(page, 'main > section')
    height = Math.min((bottom ?? 0) + 20, 900)
    await checkForbidden(page, 'order-page', height)
    const statusText = state.orderPage.statusText
    const bodyText = (await page.textContent('main')) ?? ''
    if (!bodyText.includes(statusText)) throw new Exit(5, `Статус «${statusText}» не найден на странице заказа.`)
    const enc = await encode(page, { name: `order-page-${dpr}x`, width: 390, height, dpr, budget: 120 * KB })
    result[`${dpr}x`] = enc.buf
    meta = { statusText }
    await ctx.close()
  }
  for (const k of ['1x', '2x']) writeFileSync(join(tmp, `order-page-${k}.webp`), result[k])
  return { cssHeight: height, bytes: { '1x': result['1x'].length, '2x': result['2x'].length }, ...meta }
}

async function signIn(page, webUrl, state) {
  await page.goto(`${webUrl}/login`)
  try {
    await page.getByLabel('Телефон').fill(state.owner.phone)
    await page.getByLabel('Пароль').fill(state.owner.password)
    await page.getByRole('button', { name: 'Войти' }).click()
    await page.waitForFunction(() => !location.pathname.startsWith('/login'), null, { timeout: 8000 })
  } catch {
    // Запасной путь §30.8 п. 6: логин по API и запись persist-состояния auth-store.
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

async function openBoard(page, webUrl, state) {
  await page.goto(`${webUrl}/cabinet/${state.shop.id}/orders`)
  await page.waitForSelector('section[aria-label="Новые"]', { state: 'attached' })
  // Звук: браузер мог не включить его сам. Нажимаем «Включить звук» и ждём, пока предупреждение исчезнет.
  const enable = page.getByRole('button', { name: /Включить звук/ })
  if ((await enable.count()) > 0) {
    await enable.first().click()
    await enable.first().waitFor({ state: 'detached', timeout: 5000 }).catch(() => {})
  }
  await settle(page)
}

async function shootBoards(browser, state, webUrl, tmp) {
  const out = {}
  // Десктоп
  const desk = {}
  let deskHeight = 0
  let orderCount = 0
  for (const dpr of [1, 2]) {
    const ctx = await newContext(browser, state, 1280, 900, dpr)
    const page = await ctx.newPage()
    await signIn(page, webUrl, state)
    await openBoard(page, webUrl, state)
    for (const col of ['Новые', 'Принятые', 'Готовы к выдаче']) {
      const n = await page.locator(`section[aria-label="${col}"] article`).count()
      if (n === 0) throw new Exit(5, `В колонке «${col}» нет карточек.`)
    }
    orderCount = await page.locator('section[aria-label="Новые"], section[aria-label="Принятые"], section[aria-label="Готовы к выдаче"]').evaluateAll(
      (cols) => cols.reduce((sum, c) => sum + c.querySelectorAll('article').length, 0),
    )
    const gridBottom = await page.evaluate(() => {
      const el = document.querySelector('section[aria-label="Новые"]')?.parentElement
      return el ? Math.ceil(el.getBoundingClientRect().bottom + window.scrollY) : null
    })
    deskHeight = Math.min((gridBottom ?? 820) + 24, 820)
    await checkForbidden(page, 'board-desktop', deskHeight)
    desk[`${dpr}x`] = (await encode(page, { name: `board-desktop-${dpr}x`, width: 1280, height: deskHeight, dpr, budget: 200 * KB })).buf
    await ctx.close()
  }
  for (const k of ['1x', '2x']) writeFileSync(join(tmp, `board-desktop-${k}.webp`), desk[k])
  out.desktop = { cssHeight: deskHeight, bytes: { '1x': desk['1x'].length, '2x': desk['2x'].length }, orderCount }

  // Телефон
  const ctx = await newContext(browser, state, 390, 844, 2)
  const page = await ctx.newPage()
  await signIn(page, webUrl, state)
  await openBoard(page, webUrl, state)
  if (!(await page.locator('[role="tablist"]').isVisible())) throw new Error('На телефоне не видна панель вкладок.')
  const bottom = await bottomOf(page, 'section[aria-label="Новые"]')
  const height = Math.min((bottom ?? 900) + 16, 900)
  await checkForbidden(page, 'board-phone', height)
  const enc = await encode(page, { name: 'board-phone-2x', width: 390, height, dpr: 2, budget: 120 * KB })
  writeFileSync(join(tmp, 'board-phone-2x.webp'), enc.buf)
  out.phone = { cssHeight: height, bytes: { '2x': enc.buf.length } }
  out.browserVersion = browser.version()
  await ctx.close()
  return out
}

function sourceCommit() {
  const rev = execSync('git rev-parse --short HEAD', { cwd: frontendRoot }).toString().trim()
  const dirty = execSync('git status --porcelain -- . ":(exclude)goods/src/assets/screenshots"', { cwd: frontendRoot }).toString().trim() !== ''
  return dirty ? `${rev}-dirty` : rev
}

async function main() {
  const state = loadState()
  const tmp = mkdtempSync(join(tmpdir(), 'goods-shots-'))
  const web = await startWeb(state)
  let browser
  try {
    browser = await launch()
    const order = await shootOrder(browser, state, web.url, tmp)
    const boards = await shootBoards(browser, state, web.url, tmp)
    const manifest = {
      schemaVersion: 1,
      capturedAt: new Date().toISOString().replace(/\.\d+Z$/, 'Z'),
      sourceCommit: sourceCommit(),
      browser: boards.browserVersion.startsWith('Chrome') ? boards.browserVersion : `Chrome/${boards.browserVersion}`,
      orderPage: {
        cssWidth: 390,
        cssHeight: order.cssHeight,
        files: { '1x': 'order-page-1x.webp', '2x': 'order-page-2x.webp' },
        bytes: order.bytes,
        orderNumber: state.orderPage.number,
        pickupClock: state.orderPage.pickupClock,
        statusText: order.statusText,
      },
      boardDesktop: {
        cssWidth: 1280,
        cssHeight: boards.desktop.cssHeight,
        files: { '1x': 'board-desktop-1x.webp', '2x': 'board-desktop-2x.webp' },
        bytes: boards.desktop.bytes,
        orderCount: boards.desktop.orderCount,
      },
      boardPhone: {
        cssWidth: 390,
        cssHeight: boards.phone.cssHeight,
        files: { '2x': 'board-phone-2x.webp' },
        bytes: boards.phone.bytes,
      },
    }
    // Атомарно: всё во временном каталоге прошло проверки, теперь переносим.
    mkdirSync(outDir, { recursive: true })
    for (const f of ['order-page-1x', 'order-page-2x', 'board-desktop-1x', 'board-desktop-2x', 'board-phone-2x']) {
      const dst = join(outDir, `${f}.webp`)
      writeFileSync(dst, readFileSync(join(tmp, `${f}.webp`)))
    }
    writeFileSync(join(outDir, 'screenshots.json'), JSON.stringify(manifest, null, 2) + '\n')
    console.table([
      { file: 'order-page-1x', KB: (order.bytes['1x'] / KB).toFixed(1) },
      { file: 'order-page-2x', KB: (order.bytes['2x'] / KB).toFixed(1) },
      { file: 'board-desktop-1x', KB: (boards.desktop.bytes['1x'] / KB).toFixed(1) },
      { file: 'board-desktop-2x', KB: (boards.desktop.bytes['2x'] / KB).toFixed(1) },
      { file: 'board-phone-2x', KB: (boards.phone.bytes['2x'] / KB).toFixed(1) },
    ])
    console.log(`alt кадра заказа: Страница заказа на телефоне: заказ № ${state.orderPage.number} принят магазином, получение сегодня к ${state.orderPage.pickupClock}, отмечены шаги „Заказ оформлен“ и „Магазин принял заказ“`)
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
    console.error(`capture: ${e.message}`)
    process.exit(e instanceof Exit ? e.code : 1)
  },
)
