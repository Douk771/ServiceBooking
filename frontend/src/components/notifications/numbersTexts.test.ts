// @vitest-environment node
import { describe, it, expect } from 'vitest'
import * as texts from './numbersTexts'

// Т40-L-14: ни один текст мастера, подсказки и ошибки не упоминает средства обхода блокировок.
const FORBIDDEN = /обход|обойти|vpn|впн|прокси|proxy|анонимайзер|зеркал/i

// Исходники компонентов мастера и блока «Номера» — строковые литералы в разметке тоже не должны содержать запретных слов.
const sources = import.meta.glob(
  ['./NumbersBlock.tsx', './NumberRow.tsx', './ConnectWizard.tsx', './TermsStep.tsx', './QrStep.tsx', './numbersTexts.ts'],
  { query: '?raw', import: 'default', eager: true },
) as Record<string, string>

function flatten(v: unknown): string[] {
  if (typeof v === 'string') return [v]
  if (typeof v === 'function') return [(v as (m: string) => string)('MAX')]
  if (v && typeof v === 'object') return Object.values(v).flatMap(flatten)
  return []
}

describe('Т40-L-14: страж запрещённых слов в текстах мастера', () => {
  it('текстовые константы не содержат слов об обходе блокировок', () => {
    const all = Object.values(texts).flatMap(flatten)
    expect(all.length).toBeGreaterThan(20)
    expect(all.filter((s) => FORBIDDEN.test(s))).toEqual([])
  })

  it('исходники компонентов не содержат слов об обходе блокировок', () => {
    const entries = Object.entries(sources)
    expect(entries.length).toBeGreaterThanOrEqual(5)
    // сам регэксп страж-теста в этот список не входит; в компонентах запретных слов нет вовсе
    const bad = entries.filter(([, src]) => FORBIDDEN.test(src)).map(([f]) => f)
    expect(bad).toEqual([])
  })

  it('Т40-L-13: старая формулировка про предпринимательскую деятельность не используется', () => {
    const all = Object.values(texts).flatMap(flatten).join('\n')
    expect(all).not.toMatch(/Платные функции доступны/)
  })
})
