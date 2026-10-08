import { describe, it, expect } from 'vitest'
import { deviceSiteLabel, staffPushIntro, staffPushSwitchLabel } from './staffPushTexts'

// ARCHITECTURE_CYCLE37.md §37.12.2 / §37.3.3 п. 6 — dom is the third site of the shared «Устройства и уведомления» block.
describe('staffPushTexts with the «Дома» site (cycle 37)', () => {
  it('names the bookings of houses, alone and next to the other kinds', () => {
    expect(staffPushSwitchLabel({ hasServices: false, hasOrders: false, hasStays: true })).toBe('Уведомлять меня о новых бронях домов на этом устройстве')
    expect(staffPushSwitchLabel({ hasServices: true, hasOrders: true, hasStays: true })).toBe('Уведомлять меня о новых записях, заказах и бронях домов на этом устройстве')
    expect(staffPushSwitchLabel({ hasServices: true, hasOrders: false, hasStays: true })).toContain('записях и бронях домов')
  })

  it('does not change the two-site wording (the flag is optional)', () => {
    expect(staffPushSwitchLabel({ hasServices: true, hasOrders: true })).toBe('Уведомлять меня о новых записях и заказах на этом устройстве')
    expect(staffPushSwitchLabel({ hasServices: false, hasOrders: true })).toBe('Уведомлять меня о новых заказах на этом устройстве')
    expect(staffPushSwitchLabel({ hasServices: false, hasOrders: false })).toBe('Уведомлять меня о новых записях на этом устройстве')
  })

  it('intro on dom has no salon-only sentence', () => {
    expect(staffPushIntro({ hasServices: false, hasOrders: false, hasStays: true }, 'Stays')).toMatch(/^Push о новых бронях домов приходит/)
    expect(staffPushIntro({ hasServices: false, hasOrders: false, hasStays: true }, 'Stays')).not.toContain('а не сообщения клиентам')
  })

  it('names the site of a device', () => {
    expect(deviceSiteLabel('Stays')).toBe('через dom.ezbook.ru')
    expect(deviceSiteLabel('Services')).toBe('через ezbook.ru')
    expect(deviceSiteLabel('Orders')).toBe('через goods.ezbook.ru')
  })
})
