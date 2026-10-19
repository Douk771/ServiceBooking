// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { readFileSync, existsSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { zapisLanding } from '../../../pages/home/zapisLanding'
import { bookingSlotsShot, bookingsShot } from './shots'

// Кадры главной «Записи» (scripts/screenshots/capture-zapis-screenshots.mjs) — те же проверки, что у goods (T30-15…17).
const dir = (f: string) => fileURLToPath(new URL(`./${f}`, import.meta.url))
const manifest = JSON.parse(readFileSync(dir('screenshots.json'), 'utf8'))
const KB = 1024
const FILES = ['booking-slots-1x.webp', 'booking-slots-2x.webp', 'bookings-desktop-1x.webp', 'bookings-desktop-2x.webp', 'bookings-phone-2x.webp']
const size = (f: string) => statSync(dir(f)).size

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

describe('zapis screenshots', () => {
  it('manifest shape and files exist', () => {
    expect(manifest.schemaVersion).toBe(1)
    expect(manifest.capturedAt).toBeTruthy()
    expect(manifest.sourceCommit).toBeTruthy()
    expect(manifest.bookingSlots.cssWidth).toBeLessThanOrEqual(390)
    expect(manifest.bookingsDesktop.cssWidth).toBe(1024)
    expect(manifest.bookingsPhone.cssWidth).toBe(390)
    for (const k of ['bookingSlots', 'bookingsDesktop', 'bookingsPhone'] as const) {
      const h = manifest[k].cssHeight
      expect(Number.isInteger(h) && h > 0 && h <= 1300).toBe(true)
    }
    expect(manifest.bookingSlots.serviceName).toBeTruthy()
    for (const f of FILES) expect(existsSync(dir(f)), f).toBe(true)
  })

  it('files are WebP and fit the weight budgets', () => {
    for (const f of FILES) {
      const head = readFileSync(dir(f)).subarray(0, 12)
      expect(head.toString('ascii', 0, 4), f).toBe('RIFF')
      expect(head.toString('ascii', 8, 12), f).toBe('WEBP')
    }
    expect(size('booking-slots-2x.webp')).toBeLessThanOrEqual(80 * KB)
    expect(size('bookings-desktop-2x.webp')).toBeLessThanOrEqual(200 * KB)
    expect(size('bookings-phone-2x.webp')).toBeLessThanOrEqual(140 * KB)
    expect(size('booking-slots-1x.webp')).toBeLessThanOrEqual(size('booking-slots-2x.webp'))
    expect(size('bookings-desktop-1x.webp')).toBeLessThanOrEqual(size('bookings-desktop-2x.webp'))
  })

  it('pixel size equals CSS size times density', () => {
    const cases: [string, { cssWidth: number; cssHeight: number }, number][] = [
      ['booking-slots-1x.webp', manifest.bookingSlots, 1],
      ['booking-slots-2x.webp', manifest.bookingSlots, 2],
      ['bookings-desktop-1x.webp', manifest.bookingsDesktop, 1],
      ['bookings-desktop-2x.webp', manifest.bookingsDesktop, 2],
      ['bookings-phone-2x.webp', manifest.bookingsPhone, 2],
    ]
    for (const [f, m, dpr] of cases) {
      expect(webpSize(readFileSync(dir(f))), f).toEqual({ w: m.cssWidth * dpr, h: m.cssHeight * dpr })
    }
  })

  it('the landing shows both shots instead of the placeholder', () => {
    expect(zapisLanding.clients.screenshot).toBe(bookingSlotsShot)
    expect(zapisLanding.business.screenshot).toBe(bookingsShot)
    expect(bookingSlotsShot.alt).toContain(manifest.bookingSlots.serviceName)
    expect(bookingsShot.sources?.[0].width).toBe(manifest.bookingsDesktop.cssWidth)
  })
})
