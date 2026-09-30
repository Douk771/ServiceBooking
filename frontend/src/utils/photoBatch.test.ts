// @vitest-environment node
import { describe, it, expect } from 'vitest'
import {
  PHOTO_MAX_BYTES,
  isTransientUploadError,
  overLimitText,
  photoRejectText,
  planPhotoBatch,
} from './photoBatch'

const file = (name: string, type = 'image/jpeg', size = 1000) => {
  const f = new File(['x'], name, { type })
  Object.defineProperty(f, 'size', { value: size })
  return f
}
const err = (status?: number) => ({ response: status === undefined ? undefined : { status } })

describe('planPhotoBatch', () => {
  it('accepts jpeg/png/webp in selection order', () => {
    const plan = planPhotoBatch([file('a.jpg'), file('b.png', 'image/png'), file('c.webp', 'image/webp')], 10)
    expect(plan.accepted.map((f) => f.name)).toEqual(['a.jpg', 'b.png', 'c.webp'])
    expect(plan.rejected).toEqual([])
    expect(plan.overLimit).toEqual([])
  })

  it('rejects a wrong type', () => {
    const plan = planPhotoBatch([file('a.gif', 'image/gif')], 10)
    expect(plan.accepted).toEqual([])
    expect(plan.rejected.map((r) => [r.file.name, r.reason])).toEqual([['a.gif', 'type']])
  })

  it('falls back to the extension when type is empty, case-insensitively', () => {
    const plan = planPhotoBatch([file('A.JPG', ''), file('b.txt', '')], 10)
    expect(plan.accepted.map((f) => f.name)).toEqual(['A.JPG'])
    expect(plan.rejected.map((r) => r.file.name)).toEqual(['b.txt'])
  })

  it('5 MB passes, 5 MB + 1 byte does not', () => {
    const plan = planPhotoBatch([file('ok.jpg', 'image/jpeg', PHOTO_MAX_BYTES), file('big.jpg', 'image/jpeg', PHOTO_MAX_BYTES + 1)], 10)
    expect(plan.accepted.map((f) => f.name)).toEqual(['ok.jpg'])
    expect(plan.rejected.map((r) => [r.file.name, r.reason])).toEqual([['big.jpg', 'size']])
  })

  it('truncates to the free slots, the rest is overLimit in selection order', () => {
    const files = Array.from({ length: 5 }, (_, i) => file(`f${i}.jpg`))
    const plan = planPhotoBatch(files, 2)
    expect(plan.accepted.map((f) => f.name)).toEqual(['f0.jpg', 'f1.jpg'])
    expect(plan.overLimit.map((f) => f.name)).toEqual(['f2.jpg', 'f3.jpg', 'f4.jpg'])
  })

  it('rejected files take no slot', () => {
    const plan = planPhotoBatch([file('bad.gif', 'image/gif'), file('a.jpg'), file('b.jpg')], 2)
    expect(plan.accepted.map((f) => f.name)).toEqual(['a.jpg', 'b.jpg'])
    expect(plan.overLimit).toEqual([])
  })

  it('no free slots (also negative) means everything valid is overLimit', () => {
    expect(planPhotoBatch([file('a.jpg')], 0).overLimit).toHaveLength(1)
    expect(planPhotoBatch([file('a.jpg')], -3).accepted).toEqual([])
  })
})

describe('texts', () => {
  it('overLimitText lists the names once', () => {
    expect(overLimitText([file('a.jpg'), file('b.jpg')])).toBe('Не добавлено 2 фото: в галерее не больше 10 фото: a.jpg, b.jpg')
  })
  it('reject texts', () => {
    expect(photoRejectText('size')).toBe('Файл больше 5 МБ')
    expect(photoRejectText('type')).toMatch(/JPEG, PNG или WEBP/)
  })
})

describe('isTransientUploadError', () => {
  it.each([429, 500, 503, undefined])('%s is transient', (s) => expect(isTransientUploadError(err(s))).toBe(true))
  it.each([400, 403, 404, 413])('%s is not transient', (s) => expect(isTransientUploadError(err(s))).toBe(false))
})
