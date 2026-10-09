// @vitest-environment node
import { describe, it, expect } from 'vitest'
import baniWorker from '../public/sw.js?raw'
import { describeWorkerRouting } from '@/test/workerRouting'

// ARCHITECTURE_CYCLE42.md §42.9.3 — the bani worker is the dom worker with bani defaults; the shared routing table runs against it too.
describeWorkerRouting({ name: 'bani', source: baniWorker, origin: 'https://bani.ezbook.ru', peer: 'https://ezbook.ru', fallback: '/cabinet' })

describe('bani worker: defaults of the «Бани» pushes', () => {
  it('has the one spelling of the brand and no order wording', () => {
    expect(baniWorker).toContain("'EZBOOK Бани'")
    expect(baniWorker).not.toContain('ezbook · Бани')
    expect(baniWorker).not.toMatch(/Новый заказ/)
  })
})
