import { describe, it, expect } from 'vitest'
import { createGuestMemory, GUEST_STORAGE_KEY } from './guestStorage'

const memoryStorage = (initial: Record<string, string> = {}) => {
  const data = { ...initial }
  return { data, getItem: (k: string) => data[k] ?? null, setItem: (k: string, v: string) => void (data[k] = v) }
}

describe('guest memory of a tab (Т42-09)', () => {
  it('stores exactly the name and the phone, nothing else', () => {
    const s = memoryStorage()
    createGuestMemory(s).save({ name: 'Анна', phone: '+79001112233', notifyByMessenger: true, items: [1], guestsCount: 4 } as { name: string; phone: string })
    expect(Object.keys(s.data)).toEqual([GUEST_STORAGE_KEY])
    expect(JSON.parse(s.data[GUEST_STORAGE_KEY])).toEqual({ name: 'Анна', phone: '+79001112233' })
  })

  it('reads back what it saved', () => {
    const m = createGuestMemory(memoryStorage())
    m.save({ name: 'Анна', phone: '+79001112233' })
    expect(m.load()).toEqual({ name: 'Анна', phone: '+79001112233' })
  })

  it.each([[''], ['not json'], ['null'], ['{"name":1,"phone":"x"}'], ['{"name":"a"}']])('ignores a broken record %j', (raw) => {
    expect(createGuestMemory(memoryStorage({ [GUEST_STORAGE_KEY]: raw })).load()).toBeNull()
  })

  it('works without a storage and survives one that throws', () => {
    expect(createGuestMemory(null).load()).toBeNull()
    expect(() => createGuestMemory(null).save({ name: 'a', phone: 'b' })).not.toThrow()
    const broken = {
      getItem: () => {
        throw new Error('denied')
      },
      setItem: () => {
        throw new Error('quota')
      },
    }
    const m = createGuestMemory(broken)
    expect(m.load()).toBeNull()
    expect(() => m.save({ name: 'a', phone: 'b' })).not.toThrow()
  })
})
