import type { BusinessFirstLandingConfig } from '../../components/landing/types'
import { bookingFlowCarousel, bookingsShot } from '../../assets/screenshots/zapis/shots'
import { zapisPricingLine } from '../../components/pricing/zapisPricingLine'
import { zapisFaq } from './zapisFaq'

/**
 * Конфиг главной «Записи» (ARCHITECTURE_CYCLE46_ZAPIS_LANDING.md §46.2, §46.8). Только данные: порядок секций задаёт `order`, вёрстку держит шаблон.
 * Тексты — SPEC цикла 46 §4, утверждены заказчиком 20.10.2026 как окончательные; эталон — contracts/cycle46/zapis-landing.json.
 * Правка текста = правка JSON и решение заказчика; тест zapisLanding.contract.test.tsx сверяет конфиг с JSON.
 *
 * «Подключить салон» и «Войти в кабинет»: `/cabinet/new` открыт любому вошедшему (в т.ч. клиенту без роли), гость идёт на `/register`
 * и возвращается туда (`returnTo`). `/cabinet` закрыт ролями, и клиента без роли он перенаправляет на `/cabinet/new`, так что «Войти в кабинет» (`/cabinet`) не тупик.
 */
export const zapisLanding: BusinessFirstLandingConfig = {
  order: 'business-first',
  hero: {
    eyebrow: 'Онлайн-запись для салонов и мастеров',
    title: { lead: 'Онлайн-запись в ваш салон —', accent: 'без звонков и переписок' },
    intro:
      'Клиент сам выбирает услугу, мастера и свободное время, а запись сразу появляется в расписании. Не нужно отвечать на звонки и сверять тетрадь с мессенджерами.',
    primaryAction: { kind: 'auth-route', guestTo: '/register?returnTo=%2Fcabinet%2Fnew', authedTo: '/cabinet/new', label: 'Подключить салон' },
    clientLink: { lead: 'Ищете, куда записаться?', action: { kind: 'anchor', href: '#companies', label: 'Салоны на EZBOOK' } },
  },
  catalog: { id: 'companies', ariaLabel: 'Салоны на EZBOOK' },
  business: {
    eyebrow: 'Для салона',
    title: 'Все записи и расписание мастеров — в одном месте',
    titleId: 'biz-title',
    text: 'Клиенты записываются по ссылке, со страницы салона в каталоге или через форму на вашем сайте. Каждая запись сразу попадает в расписание нужного мастера.',
    actions: [
      { kind: 'auth-route', guestTo: '/register?returnTo=%2Fcabinet%2Fnew', authedTo: '/cabinet/new', label: 'Подключить салон' },
      { kind: 'route', to: '/cabinet', label: 'Войти в кабинет' },
    ],
    benefits: [
      'Страница салона с онлайн-записью и место в каталоге EZBOOK.',
      'Расписание мастеров по датам и шаблоны рабочей недели.',
      'Перенос и отмена записи клиентом — по вашим правилам.',
      'База клиентов с заметками и фото.',
      'Отзывы клиентов после визита.',
      'Форма записи для вашего сайта.',
      'Отчёты по выручке и комиссиям мастеров — на платных тарифах.',
    ],
    screenshot: bookingsShot,
    stepsPanel: {
      title: 'Как начать принимать записи',
      titleId: 'biz-steps-title',
      steps: [
        { icon: 'store', title: 'Заведите салон и услуги', text: 'Название, адрес, услуги с ценами и длительностью.' },
        { icon: 'users', title: 'Добавьте мастеров', text: 'Пригласите мастеров и задайте им рабочие дни и часы.' },
        { icon: 'external-link', title: 'Поделитесь ссылкой', text: 'Отправьте клиентам ссылку на страницу салона или встройте форму записи на свой сайт.' },
      ],
    },
  },
  clients: {
    eyebrow: 'Для ваших клиентов',
    title: 'Клиенту хватит минуты и телефона',
    titleId: 'clients-title',
    text: 'Регистрация не нужна: несколько коротких шагов — и клиент сразу видит подтверждение записи.',
    list: {
      title: 'Что ещё может клиент',
      titleId: 'clients-list-title',
      items: [
        'Записаться на несколько услуг за один визит — до пяти.',
        'Записать ребёнка или другого человека.',
        'Перенести или отменить запись в «Моих визитах» — по правилам салона.',
        'Получить напоминание накануне визита.',
        'Оставить отзыв после визита.',
      ],
      note: 'Оплата — в салоне, EZBOOK деньги клиентов не принимает.',
    },
    screenshot: bookingFlowCarousel,
  },
  pricing: { line: zapisPricingLine, lead: 'Онлайн-запись для салона' },
  faq: { items: zapisFaq },
}
