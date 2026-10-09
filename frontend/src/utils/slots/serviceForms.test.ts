// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { isSetupDirty, moveId, setupFieldOfError, toSetupInput, validateSetup, type SetupDraft } from './serviceForms'

const draft: SetupDraft = {
  name: 'Баня',
  slug: 'banya',
  minHours: 2,
  maxHours: 6,
  stepMinutes: 60,
  bufferMinutes: 30,
  showBufferToGuests: false,
  minLeadMinutes: 60,
  prepayPercent: 30,
  cancellationPolicy: 'PreparationCosts',
  cancellationBoundaryHours: 12,
  availableForHouseBookings: true,
}
const range = { min: 3, max: 24 }

describe('validateSetup (texts of API_CONTRACT_CYCLE39.md §39.26)', () => {
  it('accepts a good draft', () => {
    expect(validateSetup(draft, range)).toEqual({})
  })

  it('names the field with the server wording', () => {
    expect(validateSetup({ ...draft, name: ' ' }, range).name).toBe('Укажите название услуги')
    expect(validateSetup({ ...draft, slug: 'Бан я' }, range).slug).toMatch(/^Адрес услуги/)
    expect(validateSetup({ ...draft, minHours: 0 }, range).minHours).toBe('Минимум часов — от 1 до 12')
    expect(validateSetup({ ...draft, maxHours: 1 }, range).maxHours).toBe('Максимум часов — от минимума до 12')
    expect(validateSetup({ ...draft, bufferMinutes: 20 }, range).bufferMinutes).toBe('Время на подготовку — от 0 до 240 минут с шагом 15')
    expect(validateSetup({ ...draft, minLeadMinutes: 45 }, range).minLeadMinutes).toBe('Минимальное время до начала — от 0 до 48 часов с шагом 30 минут')
    expect(validateSetup({ ...draft, prepayPercent: 0 }, range).prepayPercent).toBe('Предоплата — от 1 до 100 % или без предоплаты')
  })

  it('«без предоплаты» (null) is valid, the boundary is checked only for «Расходы на подготовку»', () => {
    expect(validateSetup({ ...draft, prepayPercent: null }, range)).toEqual({})
    expect(validateSetup({ ...draft, cancellationBoundaryHours: 2 }, range).cancellationBoundaryHours).toBe('Срок для полного возврата — от 3 до 24 часов до начала')
    expect(validateSetup({ ...draft, cancellationPolicy: 'NoDeductions', cancellationBoundaryHours: 2 }, range)).toEqual({})
  })
})

describe('setupFieldOfError', () => {
  it('finds the field a 400 text belongs to', () => {
    expect(setupFieldOfError('Минимум часов — от 1 до 12')).toBe('minHours')
    expect(setupFieldOfError('Срок для полного возврата — от 3 до 24 часов до начала')).toBe('cancellationBoundaryHours')
    expect(setupFieldOfError('что-то другое')).toBeNull()
  })
})

describe('toSetupInput and dirty', () => {
  it('trims and maps «без предоплаты» to null', () => {
    const body = toSetupInput({ ...draft, name: ' Баня ', prepayPercent: null })
    expect(body.name).toBe('Баня')
    expect(body.standalonePrepayPercent).toBeNull()
  })
  it('detects a change', () => {
    expect(isSetupDirty(draft, { ...draft })).toBe(false)
    expect(isSetupDirty(draft, { ...draft, maxHours: 5 })).toBe(true)
  })
})

describe('moveId', () => {
  it('swaps with the neighbour and stays put at the edges', () => {
    expect(moveId(['a', 'b', 'c'], 'b', -1)).toEqual(['b', 'a', 'c'])
    expect(moveId(['a', 'b', 'c'], 'b', 1)).toEqual(['a', 'c', 'b'])
    expect(moveId(['a', 'b', 'c'], 'a', -1)).toEqual(['a', 'b', 'c'])
    expect(moveId(['a', 'b', 'c'], 'x', 1)).toEqual(['a', 'b', 'c'])
  })
})
