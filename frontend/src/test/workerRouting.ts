import { describe, expect, it, vi } from 'vitest'

// ARCHITECTURE_CYCLE33.md §33.5.2 / §33.13.2 — the routing block of both workers, executed against a fake `self`.

type Handler = (event: unknown) => void

function boot(source: string, scriptHref: string) {
  const handlers: Record<string, Handler> = {}
  const url = new URL(scriptHref)
  const focus = vi.fn()
  const navigate = vi.fn()
  const openWindow = vi.fn().mockResolvedValue(undefined)
  const showNotification = vi.fn().mockResolvedValue(undefined)
  const ownTab = { focus, navigate }
  const fakeSelf = {
    location: { href: url.href, origin: url.origin, protocol: url.protocol },
    registration: { showNotification },
    clients: { claim: vi.fn(), matchAll: vi.fn().mockResolvedValue([ownTab]), openWindow },
    skipWaiting: vi.fn(),
    addEventListener: (name: string, fn: Handler) => {
      handlers[name] = fn
    },
  }
  new Function('self', source)(fakeSelf)

  const pending: Promise<unknown>[] = []
  const wait = (p: Promise<unknown>) => pending.push(p)
  return {
    focus,
    navigate,
    openWindow,
    showNotification,
    fakeSelf,
    async push(payload: unknown) {
      handlers.push({ data: { json: () => payload }, waitUntil: wait })
      await Promise.all(pending.splice(0))
      return showNotification.mock.calls[showNotification.mock.calls.length - 1]?.[1]?.data?.url as string
    },
    async pushRaw(data: { json: () => unknown }) {
      handlers.push({ data, waitUntil: wait })
      await Promise.all(pending.splice(0))
      return showNotification.mock.calls[showNotification.mock.calls.length - 1]?.[1]?.data?.url as string
    },
    async click(storedUrl: unknown) {
      handlers.notificationclick({ notification: { close: vi.fn(), data: { url: storedUrl } }, waitUntil: wait })
      await Promise.all(pending.splice(0))
    },
  }
}

export interface WorkerRoutingCase {
  name: string
  source: string
  origin: string
  peer: string
  fallback: string
}

/** Runs the routing table against one worker's source. Shared by the ezbook and goods worker tests. */
export function describeWorkerRouting(c: WorkerRoutingCase) {
  const { source, origin, peer, fallback } = c
  describe(`${c.name} worker routing`, () => {
    const withPeer = (p = peer) => boot(source, `${origin}/sw.js?peer=${encodeURIComponent(p)}`)

    it('relative path stays a path', async () => {
      expect(await withPeer().push({ title: 't', url: '/x?a=1#h' })).toBe('/x?a=1#h')
    })

    it('own absolute url becomes a path', async () => {
      expect(await withPeer().push({ title: 't', url: `${origin}/x?a=1` })).toBe('/x?a=1')
    })

    it('sibling url with a valid peer is kept absolute', async () => {
      expect(await withPeer().push({ title: 't', url: `${peer}/cabinet/s/orders?order=1` })).toBe(
        `${peer}/cabinet/s/orders?order=1`,
      )
    })

    it.each([
      ['no peer param', () => boot(source, `${origin}/sw.js`)],
      ['peer with a path', () => withPeer(`${peer}/path`)],
      ['peer with trailing slash', () => withPeer(`${peer}/`)],
      ['peer equal to own origin', () => withPeer(origin)],
      ['peer that is not a URL', () => withPeer('nope')],
      ['http peer for https worker', () => withPeer(peer.replace('https:', 'http:'))],
    ])('sibling url without a usable peer (%s) → default page', async (_n, make) => {
      expect(await make().push({ title: 't', url: `${peer}/x` })).toBe(fallback)
    })

    it.each(['https://evil.example/x', 'javascript:alert(1)', 12, null, ''])(
      'foreign url %j → default page',
      async (u) => {
        expect(await withPeer().push({ title: 't', url: u })).toBe(fallback)
      },
    )

    it('broken JSON still shows a notification with the default page', async () => {
      const w = withPeer()
      const url = await w.pushRaw({
        json: () => {
          throw new SyntaxError('bad')
        },
      })
      expect(w.showNotification).toHaveBeenCalledTimes(1)
      expect(url).toBe(fallback)
    })

    it('click on a sibling url opens a window and never focuses or navigates our tab', async () => {
      const w = withPeer()
      await w.click(`${peer}/cabinet/s/orders?order=1`)
      expect(w.openWindow).toHaveBeenCalledWith(`${peer}/cabinet/s/orders?order=1`)
      expect(w.focus).not.toHaveBeenCalled()
      expect(w.navigate).not.toHaveBeenCalled()
      expect(w.fakeSelf.clients.matchAll).not.toHaveBeenCalled()
    })

    it('click on a sibling url when the worker has no peer → default page in our tab, sibling untouched', async () => {
      const w = boot(source, `${origin}/sw.js`)
      await w.click(`${peer}/x`)
      expect(w.openWindow).not.toHaveBeenCalledWith(`${peer}/x`)
      expect(w.navigate).toHaveBeenCalledWith(fallback)
    })

    it('click on an own path focuses and navigates the open tab', async () => {
      const w = withPeer()
      await w.click('/x?y=1')
      expect(w.focus).toHaveBeenCalled()
      expect(w.navigate).toHaveBeenCalledWith('/x?y=1')
      expect(w.openWindow).not.toHaveBeenCalled()
    })

    it('click on a foreign url falls back to the default page', async () => {
      const w = withPeer()
      await w.click('https://evil.example/')
      expect(w.navigate).toHaveBeenCalledWith(fallback)
    })

    it('contains no fetch handler and no Cache API (R11)', () => {
      expect(source).not.toMatch(/addEventListener\s*\(\s*['"`]fetch|\bcaches\b|CacheStorage/)
    })
  })
}
