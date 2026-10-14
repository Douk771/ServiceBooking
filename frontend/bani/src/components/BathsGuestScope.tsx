import { useMemo, type ReactNode } from 'react'
import { SlotVerticalProvider, useSlotVertical } from '@/components/slots/SlotVerticalContext'
import { guestMemory } from '../utils/guestStorage'
import { BANI_GUEST_WORDS } from '../utils/baniGuestWords'

/**
 * The guest pages of bani (resource, booking) run in the app's vertical with two additions of this task: the guest's words («бронь»)
 * and the tab memory of an anonymous guest (Т42-09). Layered here, over whatever `bathsVertical` already carries, so the vertical
 * itself stays the property of the shell.
 */
export function BathsGuestScope({ children }: { children: ReactNode }) {
  const base = useSlotVertical()
  const value = useMemo(() => ({ ...base, words: { ...base.words, guest: { ...base.words.guest, ...BANI_GUEST_WORDS } }, guestMemory }), [base])
  return <SlotVerticalProvider value={value}>{children}</SlotVerticalProvider>
}
