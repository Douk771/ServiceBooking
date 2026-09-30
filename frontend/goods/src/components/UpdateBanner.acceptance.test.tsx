// Цикл 34, приёмочные сценарии по SPEC_CYCLE34_GOODS_UPDATE_BANNER.md (CY34-01..CY34-12), написаны независимо от реализации.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { UpdateBanner } from './UpdateBanner'

const FIVE_MIN = 5 * 60 * 1000
const html = (b: string) => `<html><head><script type="module" crossorigin src="/assets/${b}.js"></script></head></html>`

function addBundle(name: string | null) {
  if (!name) return
  const s = document.createElement('script')
  s.type = 'module'
  s.src = `/assets/${name}.js`
  document.head.appendChild(s)
}
function stubFetch(impl: () => unknown) {
  const f = vi.fn(impl as () => Promise<unknown>)
  vi.stubGlobal('fetch', f)
  return f
}
const ok = (body: string) => () => Promise.resolve({ ok: true, text: () => Promise.resolve(body) })
function setVisibility(v: 'visible' | 'hidden') {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => v })
}
async function fire() {
  await act(async () => {
    document.dispatchEvent(new Event('visibilitychange'))
  })
}
const BANNER = 'Доступна новая версия'

describe('CY34 UpdateBanner acceptance', () => {
  let errSpy: ReturnType<typeof vi.spyOn>
  beforeEach(() => {
    setVisibility('visible')
    errSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
  })
  afterEach(() => {
    document.head.querySelectorAll('script').forEach((s) => s.remove())
    vi.unstubAllGlobals()
    vi.useRealTimers()
    errSpy.mockRestore()
  })

  it('CY34-01 return to app with newer build shows banner and button', async () => {
    addBundle('index-OLD1')
    stubFetch(ok(html('index-NEW2')))
    render(<UpdateBanner />)
    await fire()
    expect(screen.getByText(BANNER)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Обновить' })).toBeTruthy()
  })

  it('CY34-02 fetches fresh /index.html bypassing cache', async () => {
    addBundle('index-OLD1')
    const f = stubFetch(ok(html('index-OLD1')))
    render(<UpdateBanner />)
    await fire()
    expect(f).toHaveBeenCalledTimes(1)
    const [url, init] = f.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('/index.html')
    expect(init.cache).toBe('no-store')
  })

  it('CY34-03 button reloads the page; banner alone never reloads', async () => {
    addBundle('index-OLD1')
    stubFetch(ok(html('index-NEW2')))
    const reload = vi.fn()
    vi.stubGlobal('location', { ...window.location, reload })
    render(<UpdateBanner />)
    await fire()
    expect(reload).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Обновить' }))
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('CY34-04 banner does not touch user input on the page', async () => {
    addBundle('index-OLD1')
    stubFetch(ok(html('index-NEW2')))
    const input = document.createElement('input')
    document.body.appendChild(input)
    input.value = 'draft'
    render(<UpdateBanner />)
    await fire()
    expect(screen.getByText(BANNER)).toBeTruthy()
    expect(input.value).toBe('draft')
    input.remove()
  })

  it('CY34-05 periodic check every 5 minutes', async () => {
    vi.useFakeTimers()
    addBundle('index-OLD1')
    const f = stubFetch(ok(html('index-NEW2')))
    render(<UpdateBanner />)
    await act(async () => { await vi.advanceTimersByTimeAsync(FIVE_MIN - 1000) })
    expect(f).not.toHaveBeenCalled()
    await act(async () => { await vi.advanceTimersByTimeAsync(1000) })
    expect(f).toHaveBeenCalledTimes(1)
    expect(screen.getByText(BANNER)).toBeTruthy()
  })

  it('CY34-06 hidden state does not trigger a check', async () => {
    addBundle('index-OLD1')
    const f = stubFetch(ok(html('index-NEW2')))
    render(<UpdateBanner />)
    setVisibility('hidden')
    await fire()
    expect(f).not.toHaveBeenCalled()
    expect(screen.queryByText(BANNER)).toBeNull()
  })

  it('CY34-07 same build: no banner, no console errors', async () => {
    addBundle('index-SAME')
    stubFetch(ok(html('index-SAME')))
    render(<UpdateBanner />)
    await fire()
    expect(screen.queryByText(BANNER)).toBeNull()
    expect(errSpy).not.toHaveBeenCalled()
  })

  it('CY34-08 network error and non-OK response: no banner, no console errors, next check works', async () => {
    addBundle('index-OLD1')
    const f = stubFetch(() => Promise.reject(new TypeError('Failed to fetch')))
    render(<UpdateBanner />)
    await fire()
    expect(screen.queryByText(BANNER)).toBeNull()
    f.mockImplementation(() => Promise.resolve({ ok: false, status: 503, text: () => Promise.resolve(html('index-NEW2')) }))
    await fire()
    expect(screen.queryByText(BANNER)).toBeNull()
    f.mockImplementation(ok(html('index-NEW2')) as never)
    await fire()
    expect(screen.getByText(BANNER)).toBeTruthy()
    expect(errSpy).not.toHaveBeenCalled()
  })

  it('CY34-09 dev mode (no hashed bundle): no requests, no banner', async () => {
    addBundle(null)
    const s = document.createElement('script')
    s.type = 'module'
    s.src = '/src/main.tsx'
    document.head.appendChild(s)
    const f = stubFetch(ok(html('index-NEW2')))
    vi.useFakeTimers()
    render(<UpdateBanner />)
    await fire()
    await act(async () => { await vi.advanceTimersByTimeAsync(FIVE_MIN * 2) })
    expect(f).not.toHaveBeenCalled()
    expect(screen.queryByText(BANNER)).toBeNull()
  })

  it('CY34-10 garbage index.html (no bundle, e.g. captive portal page): no banner', async () => {
    addBundle('index-OLD1')
    stubFetch(ok('<html>Wi-Fi login</html>'))
    render(<UpdateBanner />)
    await fire()
    expect(screen.queryByText(BANNER)).toBeNull()
  })

  it('CY34-11 banner respects iPhone safe-area and stops polling once shown', async () => {
    addBundle('index-OLD1')
    const f = stubFetch(ok(html('index-NEW2')))
    render(<UpdateBanner />)
    await fire()
    expect(screen.getByRole('status')).toBeTruthy()
    // jsdom отбрасывает env() как невалидное значение, поэтому safe-area проверяем по исходнику компонента.
    expect(readFileSync(resolve(__dirname, 'UpdateBanner.tsx'), 'utf8')).toContain('env(safe-area-inset-bottom)')
    await fire()
    await fire()
    expect(f).toHaveBeenCalledTimes(1)
  })

  it('CY34-12 unmount removes listeners and timer', async () => {
    vi.useFakeTimers()
    addBundle('index-OLD1')
    const f = stubFetch(ok(html('index-NEW2')))
    const { unmount } = render(<UpdateBanner />)
    unmount()
    await fire()
    await act(async () => { await vi.advanceTimersByTimeAsync(FIVE_MIN) })
    expect(f).not.toHaveBeenCalled()
  })
})

describe('CY34 nginx config (US-34-02)', () => {
  const conf = readFileSync(resolve(__dirname, '../../../../deploy/nginx/goods.ezbook.conf'), 'utf8')
  const block = (start: string) => {
    const i = conf.indexOf(start)
    expect(i).toBeGreaterThan(-1)
    let depth = 0
    for (let j = conf.indexOf('{', i); j < conf.length; j++) {
      if (conf[j] === '{') depth++
      if (conf[j] === '}' && --depth === 0) return conf.slice(i, j + 1)
    }
    throw new Error('unbalanced')
  }

  it('CY34-13 /assets/ is immutable for a year', () => {
    expect(block('location /assets/')).toMatch(/Cache-Control "public, max-age=31536000, immutable" always/)
  })
  it('CY34-14 SPA fallback location / (index.html and everything else) is no-cache', () => {
    const b = block('location / ')
    expect(b).toMatch(/Cache-Control "no-cache" always/)
    expect(b).not.toMatch(/immutable/)
  })
  it('CY34-15 /assets/ keeps security headers (add_header is not inherited)', () => {
    const b = block('location /assets/')
    expect(b).toMatch(/Strict-Transport-Security/)
    expect(b).toMatch(/X-Content-Type-Options/)
  })
})
