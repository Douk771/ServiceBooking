import { describe, it, expect } from 'vitest'
import { urlBase64ToUint8Array, arrayBufferToBase64Url } from './webPushEncoding'

describe('urlBase64ToUint8Array', () => {
  it('decodes a base64url VAPID key into raw bytes', () => {
    // "hello" in base64url, no padding.
    const bytes = urlBase64ToUint8Array('aGVsbG8')
    expect(Array.from(bytes)).toEqual([104, 101, 108, 108, 111])
  })

  it('round-trips through arrayBufferToBase64Url', () => {
    const original = new Uint8Array([0, 1, 2, 253, 254, 255, 16, 32])
    const encoded = arrayBufferToBase64Url(original.buffer)
    const decoded = urlBase64ToUint8Array(encoded)
    expect(Array.from(decoded)).toEqual(Array.from(original))
  })
})

describe('arrayBufferToBase64Url', () => {
  it('null buffer → empty string, never throws', () => {
    expect(arrayBufferToBase64Url(null)).toBe('')
  })

  it('uses base64url alphabet (no +, /, or = padding)', () => {
    // Bytes chosen so the plain-base64 encoding would contain '+' and '/'.
    const bytes = new Uint8Array([251, 255, 191])
    const encoded = arrayBufferToBase64Url(bytes.buffer)
    expect(encoded).not.toMatch(/[+/=]/)
  })
})
