import { describe, it, expect } from 'vitest'
import { applyLegalRuntimeValues } from './legalRuntimeValues'
import runtimeValueForms from '../../../contracts/legal/runtime-value-forms.json'

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

describe('applyLegalRuntimeValues — shared corpus (ARCHITECTURE_CYCLE17.md §306.2 / C15-1)', () => {
  // contracts/legal/runtime-value-forms.json is the source of truth read by BOTH the C# scanner
  // (RuntimeValueScanner.cs, RuntimeValueFormsCorpusTests.cs) and this file. Divergence between
  // the two is exactly the C15-1 defect this test exists to catch.
  for (const testCase of runtimeValueForms.cases) {
    const values = { companyName: 'Салон «Ландыш»' }
    if (testCase.frontSubstitutes) {
      it(`substitutes form "${testCase.id}"`, () => {
        const result = applyLegalRuntimeValues(testCase.html, values)
        expect(result).not.toContain('data-legal-')
        // Вторая половина того, что требует _howToUse.ts в самом корпусе. Без неё зелёной
        // проходила бы регрессия, при которой разметка исчезает, а значение не появляется:
        // «нет data-legal-» выполняется и для пустой строки.
        //
        // Значение ожидается не во всех кейсах: в `unless`-ветке при известном названии блок
        // целиком удаляется, а `when`-блок может не содержать подстановки вовсе — там ждать
        // значения неверно. Поэтому условие ровно то, при котором подстановка обязана быть
        // видна: в разметке есть data-legal-value и она не внутри удаляемой ветки.
        if (testCase.html.includes('data-legal-unless')) {
          // Ветка «название неизвестно» при известном названии обязана исчезнуть целиком —
          // пустая строка здесь правильный ответ, а не регрессия.
          expect(result.trim()).toBe('')
        } else if (testCase.html.includes('data-legal-value')) {
          expect(result).toContain('Салон «Ландыш»')
        } else {
          // `when`-блок без подстановки внутри: значения ждать неоткуда, но текст блока
          // обязан остаться — именно схлопывание в пустоту ловит этот ассерт.
          expect(result.trim()).not.toBe('')
        }
      })
    } else {
      it(`leaves form "${testCase.id}" untouched (not recognized by either side)`, () => {
        expect(applyLegalRuntimeValues(testCase.html, values)).toBe(testCase.html)
      })
    }
  }

  it('unknown value name: scanner sees it (build fails) but front leaves markup intact', () => {
    const { html } = runtimeValueForms.unknownNameCase
    expect(applyLegalRuntimeValues(html, { companyName: 'Салон «Ландыш»' })).toBe(html)
  })
})
