import { describe, expect, it } from 'vitest'
// @ts-expect-error mjs без типов
import { checkApiUrl, isSwaggerUiHtml, isWithinShootingWindow, makePassword, normalizeHost } from '../../../../scripts/screenshots/seed-goods-demo.mjs'

describe('checkApiUrl', () => {
  it('пропускает localhost и [::1]', () => {
    expect(checkApiUrl('http://localhost:55000')).toBe('http://localhost:55000')
    expect(checkApiUrl('http://[::1]:5000')).toBe('http://[::1]:5000')
  })
  it('отказывает для ezbook.ru, в т.ч. с точкой и с SHOTS_ALLOW_HOST', () => {
    for (const u of ['https://ezbook.ru', 'https://goods.ezbook.ru./']) {
      expect(() => checkApiUrl(u, 'goods.ezbook.ru.')).toThrow(/боевой/)
    }
  })
  it('отказывает чужому хосту без белого списка, пропускает с ним', () => {
    expect(() => checkApiUrl('http://10.0.0.5')).toThrow()
    expect(checkApiUrl('http://sb-stand:80', 'SB-Stand')).toBe('http://sb-stand')
  })
  it('отказывает неверной схеме и мусору', () => {
    expect(() => checkApiUrl('ftp://localhost')).toThrow()
    expect(() => checkApiUrl('не адрес')).toThrow()
  })
})

describe('isSwaggerUiHtml', () => {
  it('узнаёт Swagger UI и отвергает SPA', () => {
    expect(isSwaggerUiHtml('<html><div id="swagger-ui"></div></html>')).toBe(true)
    expect(isSwaggerUiHtml('<html><div id="root"></div></html>')).toBe(false)
    expect(isSwaggerUiHtml(null)).toBe(false)
  })
})

describe('makePassword', () => {
  it('всегда содержит цифру, заглавную и строчную', () => {
    const p = makePassword(Buffer.from('a'.repeat(18)))
    expect(p).toMatch(/\d/)
    expect(p).toMatch(/[A-Z]/)
    expect(p).toMatch(/[a-z]/)
  })
})

describe('isWithinShootingWindow / normalizeHost', () => {
  it('границы окна 07:00..20:30 по поясу', () => {
    expect(isWithinShootingWindow(new Date('2026-09-30T04:00:00Z'), 'Europe/Moscow')).toBe(true)
    expect(isWithinShootingWindow(new Date('2026-09-30T03:59:00Z'), 'Europe/Moscow')).toBe(false)
    expect(isWithinShootingWindow(new Date('2026-09-30T17:30:00Z'), 'Europe/Moscow')).toBe(true)
    expect(isWithinShootingWindow(new Date('2026-09-30T17:31:00Z'), 'Europe/Moscow')).toBe(false)
  })
  it('срезает точку', () => expect(normalizeHost('A.b.')).toBe('a.b'))
})
