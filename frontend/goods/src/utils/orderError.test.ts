// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { getGoodsErrorMessage, readConflict, readRefusal, isLoginRequired } from './orderError'
import { isSlugFormatValid, normalizeSlugInput } from './slug'

const err = (status: number, data: unknown) => ({ response: { status, data } })

describe('goods error mapping', () => {
  it('reads a JSON 409 by code and prints its message', () => {
    const e = err(409, { code: 'VersionMismatch', message: 'Заказ уже изменён — вот актуальное состояние' })
    expect(readConflict(e)?.code).toBe('VersionMismatch')
    expect(getGoodsErrorMessage(e)).toBe('Заказ уже изменён — вот актуальное состояние')
  })
  it('does not treat a string 409 as a conflict', () => {
    const e = err(409, 'Это магазин: записи, услуги и расписание для него недоступны.')
    expect(readConflict(e)).toBeNull()
    expect(getGoodsErrorMessage(e)).toBe('Это магазин: записи, услуги и расписание для него недоступны.')
  })
  it('shows the server string for 400/402/429', () => {
    expect(getGoodsErrorMessage(err(400, 'Укажите имя'))).toBe('Укажите имя')
    expect(getGoodsErrorMessage(err(429, 'Слишком много заказов подряд — попробуйте через несколько минут'))).toContain('Слишком много заказов')
  })
  it('falls back per status when the body is empty', () => {
    expect(getGoodsErrorMessage(err(403, ''))).toMatch(/прав/)
    expect(getGoodsErrorMessage(err(500, ''))).toMatch(/недоступен/)
    expect(getGoodsErrorMessage({ message: 'Network Error' })).toMatch(/Нет связи/)
  })
  it('recognises the refusal that needs a sign-in', () => {
    expect(isLoginRequired(readRefusal(err(409, { code: 'LoginRequired', message: 'x' })))).toBe(true)
    expect(isLoginRequired(readRefusal(err(409, { code: 'EmptyCart', message: 'x' })))).toBe(false)
  })
})

describe('slug helpers (goods-routes.json)', () => {
  it('validates format and length', () => {
    expect(isSlugFormatValid('shaurma-na-lenina')).toBe(true)
    expect(isSlugFormatValid('ab')).toBe(false)
    expect(isSlugFormatValid('Shaurma')).toBe(false)
    expect(isSlugFormatValid('-abc')).toBe(false)
    expect(isSlugFormatValid('a'.repeat(51))).toBe(false)
  })
  it('normalizes typed input', () => {
    expect(normalizeSlugInput('Shaurma Na  Lenina')).toBe('shaurma-na-lenina')
    expect(normalizeSlugInput('--abc')).toBe('abc')
  })
})
