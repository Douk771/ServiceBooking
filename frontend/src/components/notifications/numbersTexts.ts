// Тексты блока «Номера» и мастера подключения, которые пишет фронт (ARCHITECTURE_CYCLE40.md §40.18.6: всё остальное — поля
// сервера, дословно). Источники: API_CONTRACT_CYCLE40.md §40.33.12 (Т40-L-13, Т40-L-14), LEGAL_REVIEW_CYCLE40.md §4.1 (две
// раздельные отметки). Запретные формулировки (Т40-L-14) проверяет numbersTexts.test.ts.
import type { WizardStep } from '../../utils/channelRules'

export const NUMBERS_TITLE = 'Номера'
export const NUMBERS_EMPTY_TEXT = 'Подключение мессенджеров сейчас недоступно'
export const NUMBERS_LOAD_ERROR = 'Не удалось загрузить номера. Попробуйте ещё раз.'

export const ACTION_LABELS = {
  Pay: 'Отправить заявку',
  AcceptTerms: 'Принять условия',
  BindNumber: 'Привязать номер',
  Reconnect: 'Подключить заново',
  ReplaceNumber: 'Заменить номер',
  Unbind: 'Отвязать номер',
} as const

export const MENU_REPLACE = 'Заменить номер'
export const MENU_UNBIND = 'Отвязать номер'
export const MENU_LABEL = 'Действия с номером'

export const CONFIRM_REPLACE_TEXT = 'Старый номер будет отвязан, новый нужно будет привязать по QR-коду. Оплаченный период сохранится.'
export const CONFIRM_UNBIND_TEXT = 'Номер перестанет отправлять сообщения. Привязать его заново можно в этом же блоке.'

export const WIZARD_TITLES: Record<WizardStep, (m: string) => string> = {
  Payment: (m) => `Подключение ${m}`,
  PaymentPending: (m) => `Заявка на ${m}`,
  Terms: (m) => `Условия подключения ${m}`,
  Qr: (m) => `Привязка номера ${m}`,
  Done: (m) => `${m} подключён`,
  Unavailable: (m) => `Подключение ${m}`,
  None: (m) => `Номер ${m}`,
}

export const TERMS_TRIAL_HINT = (m: string) => `Пробный период уже включает ${m}. Перед привязкой номера примите условия`
export const PAYMENT_PENDING_TEXT =
  'Заявка отправлена. Администратор свяжется с вами для оплаты и подтвердит её — после этого привяжите номер здесь'

export const FORM_LABELS = {
  Ip: 'Индивидуальный предприниматель',
  Company: 'Юридическое лицо',
  SelfEmployed: 'Самозанятый (плательщик НПД)',
} as const

export const INN_HINT = 'ИНН нужен только для счёта и виден вам и суперадмину.'
export const INN_INVALID = 'ИНН должен содержать 10 или 12 цифр'
export const OFFER_CHECKBOX_PREFIX = 'Я ознакомлен(а) с'
export const OFFER_LINK_LABEL = 'офертой на подключение WhatsApp и MAX'
export const OFFER_CHECKBOX_SUFFIX = 'и действую в предпринимательских целях'
export const RISK_CHECKBOX = 'Я прочитал(а) и принимаю риски'
export const RISK_BOX_LABEL = 'Что вы берёте на себя'
export const TERMS_SUBMIT_PAYMENT = 'Отправить заявку'
export const TERMS_SUBMIT_TERMS = 'Принять и продолжить'
export const RISK_STALE_RETRY = 'Текст изменился, прочитайте заново'

export const QR_SCAN_ALT = (m: string) => `QR-код для привязки ${m}`
export const QR_AUTO_HINT = 'Код обновляется автоматически. Страница сама перейдёт дальше, как только вы отсканируете код.'
export const QR_ERROR = 'Не удалось получить QR-код. Попробуйте ещё раз.'
export const QR_TIMEOUT = 'Время на подключение истекло. Начните привязку заново.'
export const QR_RETRY = 'Начать заново'
export const DONE_TITLE = 'Номер подключён'
export const CLOSE = 'Закрыть'
export const CANCEL = 'Отмена'
export const DONE_BUTTON = 'Готово'
