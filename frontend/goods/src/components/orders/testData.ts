import type { StaffOrderCardDto, StaffOrderDto } from '../../types'

/** Fixture builders shared by the orders-screen unit tests (not shipped: only imported from *.test.tsx). */
export function staffCard(over: Partial<StaffOrderCardDto> = {}): StaffOrderCardDto {
  return {
    id: 'o1',
    number: 27,
    businessDate: '2026-10-05',
    createdAtUtc: '2026-10-05T10:00:00Z',
    status: 'New',
    statusText: 'Новый',
    customerName: 'Иван',
    customerPhone: '79001234567',
    customerKind: 'Guest',
    customerPhoneVerified: false,
    comment: null,
    items: [
      { id: 'i1', productId: 'p1', name: 'Шаурма классическая', unit: 'Piece', unitPrice: 250, quantityOrdered: 2, lineTotal: 500, isApproximate: false },
      { id: 'i2', productId: 'p2', name: 'Сыр твёрдый', unit: 'Weight', unitPrice: 540, weightStepGrams: 100, quantityOrdered: 500, lineTotal: 270, isApproximate: true },
    ],
    total: 770,
    totalIsApproximate: true,
    isModified: false,
    hasWeightItems: true,
    version: 3,
    pickup: { kind: 'Asap', date: '2026-10-05', startUtc: '2026-10-05T10:15:00Z', dueUtc: '2026-10-05T10:30:00Z', text: 'Как можно скорее (≈ 13:15)', isPreorder: false, isOverdue: false },
    notifyByMessenger: false,
    availableActions: ['Accept', 'Reject', 'Edit'],
    ...over,
  } as StaffOrderCardDto
}

export function staffOrder(over: Partial<StaffOrderCardDto> = {}): StaffOrderDto {
  return { ...staffCard(over), events: [] } as StaffOrderDto
}
