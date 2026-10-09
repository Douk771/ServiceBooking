import { describe, it, expect } from 'vitest'
import vectors from '../../../../contracts/cycle42/bani-vectors.json'
import { bathsVertical } from '../vertical'
import { BANI_GUEST_WORDS } from './baniGuestWords'
import { guestPushMessage } from '@/utils/slots/slotPush'

describe('bani vertical words', () => {
  it('prints the contract warning texts verbatim', () => {
    expect(bathsVertical.words.ownerWarnings).toEqual(vectors.ownerText.warningTexts)
  })
  it('the iPhone hint of a guest names «Бани», dom keeps «Дома»', () => {
    expect(guestPushMessage('ios-permission-denied', BANI_GUEST_WORDS.appName)).toContain('«Бани»')
    expect(guestPushMessage('ios-permission-denied')).toContain('«Дома»')
  })
})
