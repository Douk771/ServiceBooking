import type { SlotGuestWords } from '@/utils/slots/slotGuestWords'

/** One term for the guest of bani — «бронь», never «заказ» (LEGAL_REVIEW_CYCLE42.md, Q-L42-5). */
export const BANI_GUEST_WORDS: SlotGuestWords = {
  panelLabel: 'Бронь сеанса',
  eyebrow: 'Ваша бронь',
  titlePrefix: 'Бронь',
  notFoundTitle: 'Бронь не найдена',
  loadError: 'Не удалось загрузить бронь.',
  cancelError: 'Не удалось отменить бронь.',
  createError: 'Не удалось оформить бронь. Попробуйте ещё раз.',
  createdHeld: 'Бронь создана, время удерживается за вами. Сохраните эту ссылку — по ней бронь всегда можно открыть.',
  createdConfirmed: 'Сеанс забронирован. Сохраните эту ссылку — по ней бронь всегда можно открыть.',
  companyChecks: 'Компания проверит оплату и подтвердит бронь.',
  linkNote: 'Кто знает ссылку на эту страницу, тот видит бронь и может её отменить — не пересылайте её посторонним.',
  phoneNoteAnonymous: 'На этот номер придёт ссылка на бронь. Проверьте, что номер указан верно.',
  phoneNoteAccount: 'Бронь оформляется на номер вашего аккаунта.',
  requisitesLater: 'Реквизиты появятся на странице брони.',
}
