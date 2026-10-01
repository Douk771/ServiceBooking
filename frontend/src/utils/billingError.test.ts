// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { getBillingErrorMessage } from './billingError'

const err = (status: number, data: unknown) => ({ response: { status, data } })

describe('getBillingErrorMessage', () => {
  it('keeps the fixed texts for the existing 400/409/404 answers', () => {
    expect(getBillingErrorMessage(err(400, ''), 'x')).toBe('Проверьте выбранный состав — сервер его не принял.')
    expect(getBillingErrorMessage(err(409, 'Что-то другое'), 'x')).toBe('У подписки уже есть необработанная заявка. Обновите страницу.')
    expect(getBillingErrorMessage(err(404, ''), 'x')).toBe('Заявка не найдена — возможно, её уже обработали.')
    expect(getBillingErrorMessage(err(500, ''), 'запасной')).toBe('запасной')
  })
  it('prints the cycle-24 line texts as the server composed them', () => {
    expect(getBillingErrorMessage(err(400, 'Этот тариф из другой линейки'), 'x')).toBe('Этот тариф из другой линейки')
    const conflict = 'У вас уже есть заявка на смену тарифа «Записи» — отмените её или дождитесь решения'
    expect(getBillingErrorMessage(err(409, conflict), 'x')).toBe(conflict)
  })
})

describe('getBillingErrorMessage — demo restriction (API_CONTRACT_CYCLE28.md §599)', () => {
  it('a 403 with X-Demo-Restricted shows the demo refusal, not the generic "no rights"', () => {
    const e = { response: { status: 403, data: 'В демо-версии это действие недоступно.', headers: { 'x-demo-restricted': '1' } } }
    expect(getBillingErrorMessage(e, 'x')).toBe('В демо-версии это действие недоступно.')
  })
  it('a plain 403 without the header keeps the old wording', () => {
    expect(getBillingErrorMessage({ response: { status: 403, data: '', headers: {} } }, 'x')).toBe('Недостаточно прав для этого действия.')
  })
})
