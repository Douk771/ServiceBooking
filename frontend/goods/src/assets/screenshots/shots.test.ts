import { describe, expect, it } from 'vitest'
import { readFileSync, existsSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const dir = (f: string) => fileURLToPath(new URL(`./${f}`, import.meta.url))
const manifest = JSON.parse(readFileSync(dir('screenshots.json'), 'utf8'))
const KB = 1024
const FILES = ['order-page-1x.webp', 'order-page-2x.webp', 'board-desktop-1x.webp', 'board-desktop-2x.webp', 'board-phone-2x.webp']
const size = (f: string) => statSync(dir(f)).size

/** Pixel size from the WebP header (VP8 / VP8L / VP8X). */
function webpSize(buf: Buffer): { w: number; h: number } {
  const kind = buf.toString('ascii', 12, 16)
  if (kind === 'VP8 ') return { w: buf.readUInt16LE(26) & 0x3fff, h: buf.readUInt16LE(28) & 0x3fff }
  if (kind === 'VP8L') {
    const b = buf.readUInt32LE(21)
    return { w: (b & 0x3fff) + 1, h: ((b >> 14) & 0x3fff) + 1 }
  }
  if (kind === 'VP8X') return { w: buf.readUIntLE(24, 3) + 1, h: buf.readUIntLE(27, 3) + 1 }
  throw new Error(`unknown WebP chunk ${kind}`)
}

describe('screenshots', () => {
  it('T30-15 manifest shape and files exist', () => {
    expect(manifest.schemaVersion).toBe(1)
    expect(manifest.capturedAt).toBeTruthy()
    expect(manifest.sourceCommit).toBeTruthy()
    expect(manifest.orderPage.cssWidth).toBe(390)
    expect(manifest.boardDesktop.cssWidth).toBeGreaterThanOrEqual(1280)
    expect(manifest.boardDesktop.cssWidth).toBeLessThanOrEqual(1440)
    expect(manifest.boardPhone.cssWidth).toBe(390)
    for (const k of ['orderPage', 'boardDesktop', 'boardPhone'] as const) {
      const h = manifest[k].cssHeight
      expect(Number.isInteger(h) && h > 0 && h <= 900).toBe(true)
    }
    expect(Number.isInteger(manifest.orderPage.orderNumber) && manifest.orderPage.orderNumber > 0).toBe(true)
    expect(manifest.orderPage.pickupClock).toMatch(/^\d{2}:\d{2}$/)
    for (const f of FILES) expect(existsSync(dir(f)), f).toBe(true)
  })

  it('T30-16 files are WebP and fit the weight budgets', () => {
    for (const f of FILES) {
      const head = readFileSync(dir(f)).subarray(0, 12)
      expect(head.toString('ascii', 0, 4), f).toBe('RIFF')
      expect(head.toString('ascii', 8, 12), f).toBe('WEBP')
    }
    expect(size('order-page-2x.webp')).toBeLessThanOrEqual(120 * KB)
    expect(size('board-desktop-2x.webp')).toBeLessThanOrEqual(200 * KB)
    expect(size('board-phone-2x.webp')).toBeLessThanOrEqual(120 * KB)
    expect(size('order-page-1x.webp')).toBeLessThanOrEqual(size('order-page-2x.webp'))
    expect(size('board-desktop-1x.webp')).toBeLessThanOrEqual(size('board-desktop-2x.webp'))
    expect(size('order-page-2x.webp') + size('board-desktop-2x.webp')).toBeLessThanOrEqual(450 * KB)
  })

  it('T30-17 pixel size equals CSS size times density', () => {
    const cases: [string, { cssWidth: number; cssHeight: number }, number][] = [
      ['order-page-1x.webp', manifest.orderPage, 1],
      ['order-page-2x.webp', manifest.orderPage, 2],
      ['board-desktop-1x.webp', manifest.boardDesktop, 1],
      ['board-desktop-2x.webp', manifest.boardDesktop, 2],
      ['board-phone-2x.webp', manifest.boardPhone, 2],
    ]
    for (const [f, m, k] of cases) {
      expect(webpSize(readFileSync(dir(f))), f).toEqual({ w: m.cssWidth * k, h: m.cssHeight * k })
    }
  })
})
