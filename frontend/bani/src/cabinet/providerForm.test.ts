import { describe, it, expect } from 'vitest'
import { providerFieldOfError, validateProvider, type ProviderForm } from './providerForm'

const ok = (over: Partial<ProviderForm>): ProviderForm => ({ status: 'Organization', name: 'ООО Баня', inn: '7707083893', ogrn: '1027700132195', claimsAddress: 'Кемерово', ...over })

describe('validateProvider', () => {
  it('asks for the status first', () => {
    expect(validateProvider(ok({ status: '' }))).toEqual({ status: 'Выберите статус исполнителя' })
  })

  it('wants 10 digits of INN and 13 of OGRN from an organization', () => {
    expect(validateProvider(ok({}))).toEqual({})
    expect(validateProvider(ok({ inn: '123' })).inn).toBe('Неверный ИНН')
    expect(validateProvider(ok({ ogrn: '' })).ogrn).toBe('Укажите ОГРН')
    expect(validateProvider(ok({ ogrn: '12' })).ogrn).toBe('Неверный ОГРН')
  })

  it('wants 12 digits of INN and 15 of OGRNIP from a sole proprietor', () => {
    expect(validateProvider(ok({ status: 'IndividualEntrepreneur', inn: '500100732259', ogrn: '' })).ogrn).toBe('Укажите ОГРНИП')
    expect(validateProvider(ok({ status: 'IndividualEntrepreneur', inn: '500100732259', ogrn: '304500116000157' }))).toEqual({})
  })

  it('does not ask a self-employed person or an individual for an OGRN', () => {
    expect(validateProvider(ok({ status: 'SelfEmployed', inn: '500100732259', ogrn: '' }))).toEqual({})
    expect(validateProvider(ok({ status: 'Individual', inn: '500100732259', ogrn: '' }))).toEqual({})
  })

  it('rejects letters in the INN and an empty claims address', () => {
    expect(validateProvider(ok({ inn: '77070838a3' })).inn).toBe('Неверный ИНН')
    expect(validateProvider(ok({ claimsAddress: '  ' })).claimsAddress).toBe('Укажите адрес для претензий')
  })
})

describe('providerFieldOfError', () => {
  it('routes a 400 text to its field', () => {
    expect(providerFieldOfError('Неверный ИНН')).toBe('inn')
    expect(providerFieldOfError('Укажите ОГРНИП')).toBe('ogrn')
    expect(providerFieldOfError('Укажите адрес для претензий')).toBe('claimsAddress')
    expect(providerFieldOfError('прочее')).toBeNull()
  })
})
