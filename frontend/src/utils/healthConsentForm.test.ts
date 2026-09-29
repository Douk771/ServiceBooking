import { describe, it, expect, beforeEach } from 'vitest'
import {
  BLANK_LINE,
  blankLineForPrint,
  saveLastPrintedHealthForm,
  getLastPrintedHealthForm,
  clearLastPrintedHealthFormsForTests,
} from './healthConsentForm'

describe('blankLineForPrint', () => {
  it('replaces null with BLANK_LINE', () => {
    expect(blankLineForPrint({ operatorInn: null })).toEqual({ operatorInn: BLANK_LINE })
  })

  it('replaces undefined with BLANK_LINE too', () => {
    expect(blankLineForPrint({ operatorInn: undefined })).toEqual({ operatorInn: BLANK_LINE })
  })

  it('leaves a present value untouched', () => {
    expect(blankLineForPrint({ clientFullName: 'Мария Иванова' })).toEqual({ clientFullName: 'Мария Иванова' })
  })

  it('leaves an explicit empty string as an empty string, not a blank line', () => {
    expect(blankLineForPrint({ companyAddress: '' })).toEqual({ companyAddress: '' })
  })

  it('handles a mix of present and missing values independently', () => {
    expect(
      blankLineForPrint({ clientFullName: 'Мария Иванова', operatorFullName: null, operatorInn: null, companyName: 'Лотос' }),
    ).toEqual({
      clientFullName: 'Мария Иванова',
      operatorFullName: BLANK_LINE,
      operatorInn: BLANK_LINE,
      companyName: 'Лотос',
    })
  })
})

describe('saveLastPrintedHealthForm / getLastPrintedHealthForm', () => {
  beforeEach(() => clearLastPrintedHealthFormsForTests())

  it('returns null when nothing was ever printed for this company/client pair', () => {
    expect(getLastPrintedHealthForm('co1', 'u1')).toBeNull()
  })

  it('round-trips the last printed form', () => {
    saveLastPrintedHealthForm('co1', 'u1', { formId: 'HD-7K3M9QTX', textVersion: '2026-09-28-draft' })
    expect(getLastPrintedHealthForm('co1', 'u1')).toEqual({ formId: 'HD-7K3M9QTX', textVersion: '2026-09-28-draft' })
  })

  it('round-trips a null formId (the salon used its own paper form)', () => {
    saveLastPrintedHealthForm('co1', 'u1', { formId: null, textVersion: '2026-09-28-draft' })
    expect(getLastPrintedHealthForm('co1', 'u1')).toEqual({ formId: null, textVersion: '2026-09-28-draft' })
  })

  it('keeps different (companyId, clientKey) pairs separate', () => {
    saveLastPrintedHealthForm('co1', 'u1', { formId: 'HD-AAAAAAAA', textVersion: 'v1' })
    saveLastPrintedHealthForm('co1', 'phone:79990000000', { formId: 'HD-BBBBBBBB', textVersion: 'v1' })
    saveLastPrintedHealthForm('co2', 'u1', { formId: 'HD-CCCCCCCC', textVersion: 'v1' })

    expect(getLastPrintedHealthForm('co1', 'u1')?.formId).toBe('HD-AAAAAAAA')
    expect(getLastPrintedHealthForm('co1', 'phone:79990000000')?.formId).toBe('HD-BBBBBBBB')
    expect(getLastPrintedHealthForm('co2', 'u1')?.formId).toBe('HD-CCCCCCCC')
  })

  it('a later print for the same pair overwrites the earlier one', () => {
    saveLastPrintedHealthForm('co1', 'u1', { formId: 'HD-AAAAAAAA', textVersion: 'v1' })
    saveLastPrintedHealthForm('co1', 'u1', { formId: 'HD-BBBBBBBB', textVersion: 'v2' })
    expect(getLastPrintedHealthForm('co1', 'u1')).toEqual({ formId: 'HD-BBBBBBBB', textVersion: 'v2' })
  })

  it('never touches sessionStorage/localStorage — §441 item 2 forbids keeping formId anywhere but in-memory', () => {
    saveLastPrintedHealthForm('co1', 'phone:79990000000', { formId: 'HD-AAAAAAAA', textVersion: 'v1' })
    expect(sessionStorage.length).toBe(0)
    expect(localStorage.length).toBe(0)
  })
})
