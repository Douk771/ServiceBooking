/**
 * The guest-facing words of an order that differ between verticals: dom says «заказ», bani says «бронь» (LEGAL_REVIEW_CYCLE42.md,
 * Q-L42-5: one term in one interface). The defaults are exactly the strings dom has always shown; a vertical overrides some of them
 * in `SlotWords.guest`.
 */
export interface SlotGuestWords {
  /** aria-label of the order block. */
  panelLabel: string
  /** Eyebrow above the title of the order page. */
  eyebrow: string
  /** `document.title` prefix of the order page. */
  titlePrefix: string
  notFoundTitle: string
  loadError: string
  cancelError: string
  createError: string
  createdHeld: string
  createdConfirmed: string
  companyChecks: string
  linkNote: string
  phoneNoteAnonymous: string
  phoneNoteAccount: string
  requisitesLater: string
}

export const DEFAULT_GUEST_WORDS: SlotGuestWords = {
  panelLabel: 'Заказ услуги',
  eyebrow: 'Ваш сеанс',
  titlePrefix: 'Сеанс',
  notFoundTitle: 'Заказ не найден',
  loadError: 'Не удалось загрузить заказ.',
  cancelError: 'Не удалось отменить заказ.',
  createError: 'Не удалось оформить заказ. Попробуйте ещё раз.',
  createdHeld: 'Заказ создан, время удерживается за вами. Сохраните эту ссылку — по ней заказ всегда можно открыть.',
  createdConfirmed: 'Сеанс забронирован. Сохраните эту ссылку — по ней заказ всегда можно открыть.',
  companyChecks: 'Компания проверит оплату и подтвердит заказ.',
  linkNote: 'Кто знает ссылку на эту страницу, тот видит заказ и может его отменить — не пересылайте её посторонним.',
  phoneNoteAnonymous: 'На этот номер придёт ссылка на заказ. Проверьте, что номер указан верно.',
  phoneNoteAccount: 'Заказ оформляется на номер вашего аккаунта.',
  requisitesLater: 'Реквизиты появятся на странице заказа.',
}

/** The words of a vertical: its overrides over the defaults. */
export function resolveGuestWords(overrides?: Partial<SlotGuestWords>): SlotGuestWords {
  return { ...DEFAULT_GUEST_WORDS, ...overrides }
}
