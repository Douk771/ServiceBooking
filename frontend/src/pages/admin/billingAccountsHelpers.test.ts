// @vitest-environment node
import { describe, it, expect } from 'vitest'
import {
  optionRowsToPayload,
  computeExpectedTotal,
  buildAssignInput,
  isPaidUntilMissing,
  isManualReasonRequired,
  manualReasonValidationError,
  type AssignOptionRow,
} from './billingAccountsHelpers'

const toggleRow: AssignOptionRow = {
  optionId: 'opt-toggle',
  name: 'Аналитика',
  kind: 'Toggle',
  pricePerMonth: 300,
  selected: true,
  quantity: '1',
}

const qtyRow: AssignOptionRow = {
  optionId: 'opt-qty',
  name: 'Номер WhatsApp',
  kind: 'Quantity',
  unitName: 'номер',
  pricePerMonth: 500,
  selected: true,
  quantity: '3',
}

describe('optionRowsToPayload', () => {
  it('sends only selected rows', () => {
    const rows = [toggleRow, { ...qtyRow, selected: false }]
    expect(optionRowsToPayload(rows)).toEqual([{ optionId: 'opt-toggle', quantity: 1 }])
  })

  it('forces quantity 1 for Toggle options regardless of the stored quantity field', () => {
    const rows = [{ ...toggleRow, quantity: '5' }]
    expect(optionRowsToPayload(rows)).toEqual([{ optionId: 'opt-toggle', quantity: 1 }])
  })

  it('parses the quantity for Quantity options, flooring invalid input at 1', () => {
    expect(optionRowsToPayload([qtyRow])).toEqual([{ optionId: 'opt-qty', quantity: 3 }])
    expect(optionRowsToPayload([{ ...qtyRow, quantity: '' }])).toEqual([{ optionId: 'opt-qty', quantity: 1 }])
    expect(optionRowsToPayload([{ ...qtyRow, quantity: '0' }])).toEqual([{ optionId: 'opt-qty', quantity: 1 }])
  })
})

describe('computeExpectedTotal', () => {
  it('sums plan price plus unit price times quantity for every selected option', () => {
    const total = computeExpectedTotal(1000, [toggleRow, qtyRow], { 'opt-toggle': 300, 'opt-qty': 500 })
    expect(total).toBe(1000 + 300 * 1 + 500 * 3)
  })

  it('ignores unselected rows entirely', () => {
    const total = computeExpectedTotal(1000, [{ ...qtyRow, selected: false }], { 'opt-qty': 500 })
    expect(total).toBe(1000)
  })
})

describe('buildAssignInput', () => {
  it('turns blank amount/comment into null rather than empty strings', () => {
    const input = buildAssignInput({
      planId: 'plan-1',
      isActive: true,
      paidUntil: '2026-01-01',
      rows: [toggleRow],
      amount: '',
      comment: '   ',
      confirmLimitOverflow: false,
    })
    expect(input.amount).toBeNull()
    expect(input.comment).toBeNull()
    expect(input.options).toEqual([{ optionId: 'opt-toggle', quantity: 1 }])
  })

  it('passes requestId through so approving a request closes it in the same call', () => {
    const input = buildAssignInput({
      planId: null,
      isActive: false,
      paidUntil: null,
      rows: [],
      amount: '1500',
      comment: 'Счёт 42',
      requestId: 'req-1',
      confirmLimitOverflow: true,
    })
    expect(input).toEqual({
      planId: null,
      isActive: false,
      paidUntil: null,
      options: [],
      amount: 1500,
      comment: 'Счёт 42',
      requestId: 'req-1',
      confirmLimitOverflow: true,
    })
  })
})

describe('isPaidUntilMissing', () => {
  it('requires a date whenever a paid plan is selected', () => {
    expect(isPaidUntilMissing('plan-1', '')).toBe(true)
  })

  it('is satisfied once a date is present for a paid plan', () => {
    expect(isPaidUntilMissing('plan-1', '2026-01-01')).toBe(false)
  })

  it('never requires a date for the free plan (empty planId)', () => {
    expect(isPaidUntilMissing('', '')).toBe(false)
  })
})

describe('buildAssignInput — free plan never carries an expiry', () => {
  it('forces paidUntil to null when planId is null (free plan), even if a stale date is still in state', () => {
    const input = buildAssignInput({
      planId: null,
      isActive: true,
      paidUntil: '2026-01-01',
      rows: [],
      amount: '',
      comment: '',
      confirmLimitOverflow: false,
    })
    expect(input.planId).toBeNull()
    expect(input.paidUntil).toBeNull()
  })

  it('keeps the given date for a paid plan', () => {
    const input = buildAssignInput({
      planId: 'plan-1',
      isActive: true,
      paidUntil: '2026-01-01',
      rows: [],
      amount: '',
      comment: '',
      confirmLimitOverflow: false,
    })
    expect(input.paidUntil).toBe('2026-01-01')
  })
})

// API_CONTRACT_CYCLE20.md §433.1 (US-20-02, П3) / ARCHITECTURE_CYCLE20.md §403.2 — client-side mirror
// of `ManualPlanAssignmentPolicy.RequiresReason`/`.Validate`. The server re-checks everything; these
// tests just pin the UI hint to the same six cases CY20-U-02 pins on the backend.
describe('isManualReasonRequired', () => {
  it('requires a reason for a hidden plan different from the current one', () => {
    expect(isManualReasonRequired('plan-current', 'plan-hidden', false)).toBe(true)
  })

  it('does not require a reason for the SAME hidden plan (renewal)', () => {
    expect(isManualReasonRequired('plan-hidden', 'plan-hidden', false)).toBe(false)
  })

  it('does not require a reason for a public plan', () => {
    expect(isManualReasonRequired('plan-current', 'plan-public', true)).toBe(false)
  })

  it('does not require a reason for Free (no target plan)', () => {
    expect(isManualReasonRequired('plan-current', null, undefined)).toBe(false)
  })

  it('treats a plan not yet loaded (isPublic undefined) as not hidden — no reason demanded before data arrives', () => {
    expect(isManualReasonRequired('plan-current', 'plan-x', undefined)).toBe(false)
  })
})

describe('manualReasonValidationError', () => {
  it('demands a reason code when one is required and none was picked', () => {
    expect(manualReasonValidationError('', '', true)).toMatch(/основание/)
  })

  it('is satisfied when a reason is required and OperatorErrorCorrection has details', () => {
    expect(manualReasonValidationError('OperatorErrorCorrection', 'опечатка в счёте', true)).toBeNull()
  })

  it('rejects OperatorErrorCorrection without details, even when not otherwise required', () => {
    expect(manualReasonValidationError('OperatorErrorCorrection', '  ', false)).toMatch(/опишите/i)
  })

  it('rejects TrialReissue outright — this form never assigns the trial plan', () => {
    expect(manualReasonValidationError('TrialReissue', 'что угодно', true)).toMatch(/выдать повторно/i)
  })

  it('rejects details longer than 1000 characters', () => {
    expect(manualReasonValidationError('OperatorErrorCorrection', 'а'.repeat(1001), true)).toMatch(/1000/)
  })

  it('allows an empty reason when none is required', () => {
    expect(manualReasonValidationError('', '', false)).toBeNull()
  })
})

describe('buildAssignInput — reason fields (US-20-02)', () => {
  it('leaves reasonCode/reasonDetails undefined when neither is given, so JSON.stringify omits them (unchanged wire shape)', () => {
    const input = buildAssignInput({
      planId: 'plan-1',
      isActive: true,
      paidUntil: '2026-01-01',
      rows: [],
      amount: '',
      comment: '',
      confirmLimitOverflow: false,
    })
    expect(input.reasonCode).toBeUndefined()
    expect(input.reasonDetails).toBeUndefined()
    expect(JSON.stringify(input)).not.toMatch(/reason/)
  })

  it('carries reasonCode and trims reasonDetails through to the request body', () => {
    const input = buildAssignInput({
      planId: 'plan-1',
      isActive: true,
      paidUntil: '2026-01-01',
      rows: [],
      amount: '',
      comment: '',
      confirmLimitOverflow: false,
      reasonCode: 'OperatorErrorCorrection',
      reasonDetails: '  оплата не применилась из-за сбоя импорта  ',
    })
    expect(input.reasonCode).toBe('OperatorErrorCorrection')
    expect(input.reasonDetails).toBe('оплата не применилась из-за сбоя импорта')
  })
})

