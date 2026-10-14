/**
 * The cabinet and staff words of an order that differ between verticals: dom says «Ручной заказ», «Сеанс», «Услуги»; bani says «бронь» and
 * «ресурс» (LEGAL_REVIEW_CYCLE42.md §3.3, §10: one term in one interface). The defaults are exactly the strings dom has always shown;
 * a vertical overrides some of them in `SlotWords.cabinet`.
 */
export interface SlotCabinetWords {
  /** Button that opens the manual order dialog. */
  manualOrder: string
  /** Title of the manual order dialog (while loading, on error, empty) and of its first step. */
  manualOrderTitle: string
  manualOrderStepTitle: string
  manualOrderCreateError: string
  manualOrderNoServices: string
  /** Title of the list of services and the breadcrumb back to it. */
  servicesTitle: string
  servicesLoadError: string
  addService: string
  /** Link to the service day. */
  dayLink: string
  /** Title of the session on the order page and in the cabinet (card, tab title, not-found). */
  sessionTitle: string
  sessionNotFound: string
  allServices: string
  newServiceTitle: string
  createService: string
  dayEmptyTitle: string
  /** The list of orders of the cabinet: empty lines of the three presets, the load error, the aria-label of the tabs. */
  ordersEmptyAwaiting: string
  ordersEmptyHeld: string
  ordersEmptyConfirmed: string
  ordersLoadError: string
  ordersStatusLabel: string
}

export const DEFAULT_CABINET_WORDS: SlotCabinetWords = {
  manualOrder: 'Ручной заказ',
  manualOrderTitle: 'Ручной заказ',
  manualOrderStepTitle: 'Ручной заказ услуги',
  manualOrderCreateError: 'Не удалось создать заказ.',
  manualOrderNoServices: 'У компании нет услуг. Создайте услугу в разделе «Услуги».',
  servicesTitle: 'Услуги',
  servicesLoadError: 'Не удалось загрузить услуги.',
  addService: 'Добавить услугу',
  dayLink: 'День услуг',
  sessionTitle: 'Сеанс',
  sessionNotFound: 'Сеанс не найден',
  allServices: 'Все услуги',
  newServiceTitle: 'Новая услуга',
  createService: 'Создать услугу',
  dayEmptyTitle: 'Услуг пока нет',
  ordersEmptyAwaiting: 'Нет заказов услуг, ожидающих проверки оплаты',
  ordersEmptyHeld: 'Нет заказов услуг, которые ждут оплаты',
  ordersEmptyConfirmed: 'Нет подтверждённых заказов услуг',
  ordersLoadError: 'Не удалось загрузить заказы услуг.',
  ordersStatusLabel: 'Статус заказа услуги',
}

/** The words of a vertical: its overrides over the defaults. */
export function resolveCabinetWords(overrides?: Partial<SlotCabinetWords>): SlotCabinetWords {
  return { ...DEFAULT_CABINET_WORDS, ...overrides }
}
