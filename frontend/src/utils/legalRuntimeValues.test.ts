import { describe, it, expect } from 'vitest'
import { applyLegalRuntimeValues } from './legalRuntimeValues'

describe('applyLegalRuntimeValues — ARCHITECTURE_CYCLE15.md §256', () => {
  it('fills in a plain data-legal-value span with the escaped value', () => {
    const html = 'компании «<span data-legal-value="companyName"></span>»'
    expect(applyLegalRuntimeValues(html, { companyName: 'Гвоздь' })).toBe('компании «Гвоздь»')
  })

  it('escapes HTML in the value — a company name is never treated as markup (XSS)', () => {
    const html = '<span data-legal-value="companyName"></span>'
    expect(applyLegalRuntimeValues(html, { companyName: '<img src=x onerror=alert(1)>' })).toBe(
      '&lt;img src=x onerror=alert(1)&gt;',
    )
  })

  it('keeps the data-legal-when block when the value is known and non-empty', () => {
    const html = '<span data-legal-when="companyName">компании «<span data-legal-value="companyName"></span>»</span>'
    expect(applyLegalRuntimeValues(html, { companyName: 'Гвоздь' })).toBe('компании «Гвоздь»')
  })

  it('drops the data-legal-when block when the value is null', () => {
    const html = '<span data-legal-when="companyName">компании «X»</span><span data-legal-unless="companyName">компании, в которую вы записываетесь,</span>'
    expect(applyLegalRuntimeValues(html, { companyName: null })).toBe('компании, в которую вы записываетесь,')
  })

  it('drops the data-legal-when block when the value is an empty string', () => {
    const html = '<span data-legal-when="companyName">X</span><span data-legal-unless="companyName">Y</span>'
    expect(applyLegalRuntimeValues(html, { companyName: '' })).toBe('Y')
  })

  it('drops the data-legal-unless block when the value is known', () => {
    const html = '<span data-legal-when="companyName">X</span><span data-legal-unless="companyName">Y</span>'
    expect(applyLegalRuntimeValues(html, { companyName: 'Гвоздь' })).toBe('X')
  })

  it('leaves an unknown value name untouched, rather than blanking it (caught at build time instead)', () => {
    const html = '<span data-legal-value="somethingElse"></span>'
    expect(applyLegalRuntimeValues(html, { companyName: 'Гвоздь' })).toBe(html)
  })

  it('is pure — does not mutate its input and produces a fresh string per call (R2: two companies in one session)', () => {
    const html = '<span data-legal-value="companyName"></span>'
    const first = applyLegalRuntimeValues(html, { companyName: 'Салон А' })
    const second = applyLegalRuntimeValues(html, { companyName: 'Салон Б' })
    expect(first).toBe('Салон А')
    expect(second).toBe('Салон Б')
  })
})
