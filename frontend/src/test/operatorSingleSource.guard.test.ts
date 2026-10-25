// @vitest-environment node
import { describe, expect, it } from 'vitest'
import operatorPublic from '../../../contracts/legal/operator-public.json'

// CY48-02 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.2 item 4, §48.13.1): the public requisites of the operator live ONLY in
// contracts/legal/operator-public.json. No source file of the five frontends may repeat them as a literal.
const sources = import.meta.glob(
  [
    '../**/*.{ts,tsx}',
    '../../goods/src/**/*.{ts,tsx}',
    '../../dom/src/**/*.{ts,tsx}',
    '../../bani/src/**/*.{ts,tsx}',
    '../../about/src/**/*.{ts,tsx}',
    '!../**/*.test.{ts,tsx}',
    '!../../*/src/**/*.test.{ts,tsx}',
    '!../utils/operatorRequisites.ts',
  ],
  { query: '?raw', import: 'default', eager: true },
) as Record<string, string>

const v = operatorPublic.values
const digits = v['ТЕЛЕФОН_ОПЕРАТОРА'].replace(/\D/g, '') // 79132258915
const local = digits.slice(1) // 9132258915
const surname = v['НАИМЕНОВАНИЕ_ОПЕРАТОРА'].split(' ')[0]
const spacedPhone = new RegExp(local.split('').join('[\\s\\-()]*'))

const needles: Array<[string, (src: string) => boolean]> = [
  ['ИНН', (s) => s.includes(v['ИНН_ОПЕРАТОРА'])],
  ['телефон (цифры подряд)', (s) => s.includes(local)],
  ['телефон (с пробелами)', (s) => spacedPhone.test(s)],
  ['почта поддержки', (s) => s.includes(v['ПОЧТА_ПОДДЕРЖКИ'])],
  ['почта для обращений', (s) => s.includes(v['ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ'].split('@')[0] + '@')],
  ['фамилия оператора', (s) => s.includes(surname)],
]

describe('CY48-02 operator requisites have a single source', () => {
  it('scans a meaningful number of files', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(300)
    expect(Object.keys(sources).some((k) => k.includes('.test.'))).toBe(false)
    expect(Object.keys(sources).some((k) => k.endsWith('utils/operatorRequisites.ts'))).toBe(false)
  })

  it('no source file contains a requisite literal', () => {
    const hits: string[] = []
    for (const [file, src] of Object.entries(sources)) {
      for (const [name, test] of needles) if (test(src)) hits.push(`${file}: ${name}`)
    }
    expect(hits, 'move the value to contracts/legal/operator-public.json and use OPERATOR / fillOperatorTemplate').toEqual([])
  })

  it('operator-public.json has no empty value and no unfilled marker (DO-48-1)', () => {
    for (const [key, value] of Object.entries(v)) {
      expect(value.trim(), key).not.toBe('')
      expect(value, key).not.toContain('ЗАПОЛНИТЬ')
    }
  })
})
