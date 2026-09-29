import { Link } from 'react-router-dom'
import { useAuthStore } from '@/store/authStore'
import { Icon } from '@/components/ui/Icon'

const steps = [
  { n: '01', title: 'Соберите каталог', text: 'Категории, товары с фото, штучные и весовые — с ценой за штуку или за килограмм.', offset: '' },
  { n: '02', title: 'Дайте ссылку или QR', text: 'Покупатель открывает магазин с телефона, собирает корзину и оформляет заказ за один экран.', offset: 'md:ml-10' },
  { n: '03', title: 'Собирайте и выдавайте', text: 'Экран заказов со звуком у кассы, правка состава, точная сумма по фактическому весу при выдаче.', offset: 'md:ml-20' },
]

/** US-23-08 — short "where am I" page for the root of goods.ezbook.ru. No shop directory in cycle 1. */
export function LandingPage() {
  const authed = useAuthStore((s) => s.isAuthenticated())

  return (
    <main className="max-w-[1180px] mx-auto px-4 sm:px-8 pt-12 md:pt-20">
      <div className="grid md:grid-cols-[1.15fr_0.85fr] gap-12 md:gap-16 items-center">
        <div>
          <p className="text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark mb-5">Заказы с самовывозом</p>
          <h1 className="font-serif text-[40px] sm:text-[54px] leading-[1.06] font-medium text-ink">
            Магазин и столовая принимают заказы без звонков
          </h1>
          <p className="mt-6 text-[17px] leading-relaxed text-ink-soft max-w-[520px]">
            Покупатель открывает ваш магазин по ссылке или QR-коду, собирает заказ и следит за ним на странице. Вы видите
            новые заказы на планшете у кассы и выдаёте их с точной суммой.
          </p>
          <div className="mt-9 flex flex-wrap items-center gap-3">
            <Link
              to={authed ? '/cabinet/new' : '/register?returnTo=%2Fcabinet%2Fnew'}
              className="inline-flex items-center gap-2 bg-ink hover:bg-ink/90 !text-cream text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors"
            >
              Открыть магазин
              <Icon name="arrow-right" size={16} />
            </Link>
            {!authed && (
              <Link
                to="/login"
                className="inline-flex items-center bg-white hover:bg-cream-deep !text-ink border border-line text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors"
              >
                Войти
              </Link>
            )}
          </div>
          <p className="mt-6 text-sm text-muted max-w-[460px]">
            Каталога магазинов здесь нет: покупатели приходят по ссылке или QR-коду, которые вы разместите на кассе и
            витрине.
          </p>
        </div>

        {/* Decorative order ticket — an illustration of the product, not real data. */}
        <div aria-hidden="true" className="relative mx-auto w-full max-w-[380px] md:rotate-[1.6deg]">
          <div className="absolute inset-0 translate-x-3 translate-y-3 rounded-3xl bg-cream-deep border border-line" />
          <div className="relative rounded-3xl bg-white border border-line shadow-card p-7">
            <div className="flex items-start justify-between">
              <div>
                <p className="text-xs uppercase tracking-wider text-muted">Заказ</p>
                <p className="font-serif text-[44px] leading-none text-ink mt-1">№ 27</p>
              </div>
              <span className="inline-block px-3 py-1 rounded-full text-xs font-semibold bg-success-bg text-success">
                Готов к выдаче
              </span>
            </div>
            <ul className="mt-6 space-y-3 text-sm text-ink-soft">
              <li className="flex justify-between gap-4">
                <span>Шаурма классическая × 2</span>
                <span className="text-ink font-medium">500 ₽</span>
              </li>
              <li className="flex justify-between gap-4">
                <span>Сыр твёрдый, 300 г</span>
                <span className="text-ink font-medium">≈ 162 ₽</span>
              </li>
            </ul>
            <div className="mt-6 pt-5 border-t border-line flex items-baseline justify-between">
              <span className="text-sm text-ink-soft">Итого</span>
              <span className="font-serif text-2xl text-ink">≈ 662 ₽</span>
            </div>
          </div>
        </div>
      </div>

      <ol className="mt-20 md:mt-28 space-y-5 max-w-[760px]">
        {steps.map((s) => (
          <li key={s.n} className={`flex gap-6 items-baseline border-t border-line pt-5 ${s.offset}`}>
            <span className="font-serif text-2xl text-gold shrink-0 w-10">{s.n}</span>
            <div>
              <h2 className="text-lg font-semibold text-ink">{s.title}</h2>
              <p className="mt-1 text-[15px] text-ink-soft leading-relaxed">{s.text}</p>
            </div>
          </li>
        ))}
      </ol>
    </main>
  )
}
