// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { safeReturnTo, withReturnTo } from './returnTo'

describe('safeReturnTo', () => {
  it('accepts a relative path with query', () => {
    expect(safeReturnTo('/shaurma?checkout=1')).toBe('/shaurma?checkout=1')
    expect(safeReturnTo('/cabinet/new')).toBe('/cabinet/new')
  })
  it('falls back for empty, absolute, protocol-relative and backslash forms', () => {
    expect(safeReturnTo(null)).toBe('/')
    expect(safeReturnTo('')).toBe('/')
    expect(safeReturnTo('https://evil.example')).toBe('/')
    expect(safeReturnTo('//evil.example')).toBe('/')
    expect(safeReturnTo('/\\evil.example')).toBe('/')
    expect(safeReturnTo('javascript:alert(1)')).toBe('/')
    expect(safeReturnTo('/a\nb')).toBe('/')
    expect(safeReturnTo('/a\\b')).toBe('/')
    expect(safeReturnTo('/\\/evil.example')).toBe('/')
  })
  it('honours a custom fallback', () => {
    expect(safeReturnTo('x', '/home')).toBe('/home')
  })
})

describe('withReturnTo', () => {
  it('carries a safe value over, encoded', () => {
    expect(withReturnTo('/login', '/shop?checkout=1')).toBe('/login?returnTo=%2Fshop%3Fcheckout%3D1')
  })
  it('leaves the path untouched for an unsafe or missing value', () => {
    expect(withReturnTo('/login', '//evil')).toBe('/login')
    expect(withReturnTo('/login', null)).toBe('/login')
  })
})
