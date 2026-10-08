import { describe, expect, it } from 'vitest'
import { toOrdersGridView } from './ordersPricingLine'
import type { OrdersPublicPricingDto } from '../api/ordersPricing'

const plan = (over: Partial<OrdersPublicPricingDto['plans'][number]> = {}): OrdersPublicPricingDto['plans'][number] => ({
  id: 'p', name: 'Лавка', description: null, pricePerMonth: 690, highlights: [], includedShops: 1, includedMembers: 5,
  includedProductsPerShop: 300, includedOrdersPerMonth: 1500, sortOrder: 20, isFree: false, ...over,
})
const dto = (plans: OrdersPublicPricingDto['plans']): OrdersPublicPricingDto => ({
  version: 'v', currency: 'RUB', plans, notice: 'n', legalNotice: null,
})

const norm = (l: readonly string[]) => l.map((x) => x.replace(/\s/g, ' '))

describe('toOrdersGridView (T37-13)', () => {
  it('maps all four limit lines in order', () => {
    const v = toOrdersGridView(dto([plan()]))
    expect(norm(v.plans[0].limitLines)).toEqual(['до 1 магазина', 'до 5 участников', 'до 300 товаров в магазине', 'до 1 500 заказов в месяц'])
    expect(v.options).toEqual([])
    expect(v.plans[0].isTrial).toBe(false)
  })
  it('null shops/members/orders become "без ограничений"; products never', () => {
    const v = toOrdersGridView(dto([plan({ includedShops: null, includedMembers: null, includedOrdersPerMonth: null, includedProductsPerShop: 1000 })]))
    expect(norm(v.plans[0].limitLines)).toEqual(['Магазины без ограничений', 'Участники без ограничений', 'до 1 000 товаров в магазине', 'Заказы без ограничений'])
  })
})
