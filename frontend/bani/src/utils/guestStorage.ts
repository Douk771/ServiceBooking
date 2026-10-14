import type { SlotGuestMemory } from '@/components/slots/SlotVerticalContext'

/**
 * Name and phone of an anonymous guest, kept for the life of ONE tab so «Забронировать ещё в этом комплексе» does not ask again
 * (ARCHITECTURE_CYCLE42.md §42.10.4, Т42-09). `sessionStorage` only: it dies with the tab and never reaches a URL, a cookie or the
 * server. The messenger tick and the positions are deliberately NOT stored — a new form starts them off.
 */
export const GUEST_STORAGE_KEY = 'bani:guest'

export function createGuestMemory(storage: Pick<Storage, 'getItem' | 'setItem'> | null = safeSessionStorage()): SlotGuestMemory {
  return {
    load() {
      try {
        const raw = storage?.getItem(GUEST_STORAGE_KEY)
        if (!raw) return null
        const v = JSON.parse(raw) as unknown
        if (!v || typeof v !== 'object') return null
        const { name, phone } = v as Record<string, unknown>
        if (typeof name !== 'string' || typeof phone !== 'string') return null
        return { name, phone }
      } catch {
        return null
      }
    },
    save(guest) {
      try {
        // Exactly two fields: whatever else the caller holds must not land in the storage.
        storage?.setItem(GUEST_STORAGE_KEY, JSON.stringify({ name: guest.name, phone: guest.phone }))
      } catch {
        /* private mode / quota: the guest simply types the details again */
      }
    },
  }
}

function safeSessionStorage(): Storage | null {
  try {
    return typeof sessionStorage === 'undefined' ? null : sessionStorage
  } catch {
    return null
  }
}

export const guestMemory = createGuestMemory()
