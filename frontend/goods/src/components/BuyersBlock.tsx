import { Link } from 'react-router-dom'
import { Icon } from '@/components/ui/Icon'
import { ScreenshotFigure } from './ScreenshotFigure'
import { orderPageShot } from '../assets/screenshots/shots'

const steps = [
  { icon: 'store', title: 'Выберите магазин', text: 'Найдите магазин или кафе в каталоге своего города или откройте его по ссылке либо QR-коду.' },
  { icon: 'shopping-bag', title: 'Соберите корзину', text: 'Добавьте товары — поштучно или на вес. Сумма за весовой товар уточнится при выдаче.' },
  { icon: 'clock', title: 'Выберите время и оформите', text: 'Укажите, когда заберёте заказ — как можно скорее или к удобному часу. Всё на одном экране.' },
] as const

const track = [
  'После оформления откроется страница заказа: номер, статус и время получения.',
  'Страница обновляется сама — вы увидите, когда магазин примет заказ и когда он будет готов к выдаче.',
  'Включите уведомление на странице заказа, если браузер это разрешает, — тогда о смене статуса сообщим без обновления страницы.',
  'Сохраните ссылку на заказ. Если вы вошли в аккаунт, все заказы будут в разделе «Мои заказы».',
  'Если магазин изменит состав заказа, на странице будет видно, что было и что стало.',
] as const

/**
 * «Для покупателей» — block of the catalog home before «Для бизнеса» (ARCHITECTURE_CYCLE30.md §30.4).
 * Markup mirrors BusinessBlock.tsx; parity of classes is held by the T30-13 test.
 */
export function BuyersBlock() {
  return (
    <section aria-labelledby="buyers-title" className="mt-20 md:mt-28 border-t border-line pt-12">
      <p className="text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark mb-4">Для покупателей</p>
      <div className="max-w-[720px]">
        <h2
          id="buyers-title"
          tabIndex={-1}
          className="font-serif text-[32px] sm:text-[40px] leading-[1.1] font-medium text-ink scroll-mt-24 focus:outline-none"
        >
          Соберите заказ с телефона и заберите, когда он готов
        </h2>
        <p className="mt-5 text-[16px] leading-relaxed text-ink-soft max-w-[480px]">
          Выберите магазин или кафе, добавьте товары в корзину и укажите, когда удобно забрать. Звонить не нужно: магазин увидит заказ сразу, а вы — когда он будет готов.
        </p>
        <div className="mt-7 flex flex-wrap items-center gap-3">
          <a
            href="#shop-list"
            className="inline-flex items-center gap-2 bg-ink hover:bg-ink/90 !text-cream text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors"
          >
            Выбрать магазин
            <Icon name="arrow-right" size={16} aria-hidden />
          </a>
          <Link
            to="/orders"
            className="inline-flex items-center bg-white hover:bg-cream-deep !text-ink border border-line text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors"
          >
            Мои заказы
          </Link>
        </div>
      </div>
      <div className="mt-12 md:mt-16 rounded-[28px] bg-cream-deep px-6 py-10 sm:px-10 md:px-12 md:py-14">
        <h3 id="buyers-steps-title" className="font-serif text-[26px] sm:text-[28px] leading-[1.2] font-medium text-ink mb-8 md:mb-10">
          Как сделать заказ
        </h3>
        <ol aria-labelledby="buyers-steps-title" className="list-none grid md:grid-cols-3 gap-10">
          {steps.map((s) => (
            <li key={s.title}>
              <div className="w-[46px] h-[46px] rounded-full bg-cream border border-line-strong flex items-center justify-center mb-5">
                <Icon name={s.icon} size={20} strokeWidth={1.6} className="text-gold-dark" aria-hidden />
              </div>
              <h4 className="font-serif text-[21px] font-medium mb-2.5 text-ink">{s.title}</h4>
              <p className="text-[15px] leading-[1.6] text-ink-soft">{s.text}</p>
            </li>
          ))}
        </ol>
      </div>
      <div className="mt-12 md:mt-16 grid md:grid-cols-[1.05fr_0.95fr] gap-10 md:gap-16 items-start">
        <div>
          <h3 id="buyers-track-title" className="font-serif text-[26px] sm:text-[28px] leading-[1.2] font-medium text-ink mb-6">
            Как следить за заказом
          </h3>
          <ul aria-labelledby="buyers-track-title" className="space-y-4">
            {track.map((t) => (
              <li key={t} className="flex items-start gap-3 text-[15px] leading-[1.6] text-ink">
                <Icon name="check" size={18} className="text-gold-dark shrink-0 mt-[3px]" aria-hidden />
                <span>{t}</span>
              </li>
            ))}
          </ul>
          <p className="mt-6 text-[13px] text-muted">Оплата — при получении в магазине.</p>
        </div>
        <ScreenshotFigure {...orderPageShot} className="mx-auto w-full max-w-[320px] md:max-w-[340px]" />
      </div>
    </section>
  )
}
