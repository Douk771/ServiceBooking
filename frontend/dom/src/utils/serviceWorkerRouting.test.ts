// @vitest-environment node
import { describe, it, expect } from 'vitest'
import domWorker from '../../public/sw.js?raw'
import { describeWorkerRouting } from '@/test/workerRouting'

// ARCHITECTURE_CYCLE37.md §37.14.6 — the dom worker is the goods worker with dom defaults; the shared routing table runs against it too.
describeWorkerRouting({ name: 'dom', source: domWorker, origin: 'https://dom.ezbook.ru', peer: 'https://ezbook.ru', fallback: '/cabinet' })

describe('dom worker: defaults of the «Дома» pushes (API_CONTRACT_CYCLE37.md §37.33)', () => {
  it('has the dom title and no order wording', () => {
    expect(domWorker).toContain("'ezbook · Дома'")
    expect(domWorker).not.toMatch(/Новый заказ/)
  })

  it('has no fetch handler and no Cache API — CI greps this file too', () => {
    expect(domWorker).not.toMatch(/addEventListener\s*\(\s*['"`]fetch|\bcaches\b|CacheStorage/)
  })
})
