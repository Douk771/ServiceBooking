import { Link } from 'react-router-dom'
import { useAuthStore } from '@/store/authStore'
import { Icon } from '@/components/ui/Icon'

const steps = [
  { n: '01', title: 'Соберите каталог', text: 'Категории, товары с фото, штучные и весовые — с ценой за штуку или за килограмм.', offset: '' },
  { n: '02', title: 'Дайте ссылку или QR', text: 'Покупатель открывает магазин с телефона, собирает корзину и оформляет заказ за один экран.', offset: 'md:ml-10' },
  { n: '03', title: 'Собирайте и выдавайте', text: 'Экран заказов со звуком у кассы, лист сборки, история и сводка, точная сумма по фактическому весу при выдаче.', offset: 'md:ml-20' },
]

/** «Для бизнеса» — the former landing text, now the bottom block of the catalog home (ARCHITECTURE_CYCLE25.md §505.4). */
export function BusinessBlock() {
  const authed = useAuthStore((s) => s.isAuthenticated())
  return (
    <section aria-labelledby="biz-title" className="mt-20 md:mt-28 border-t border-line pt-12">
      <p className="text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark mb-4">Для бизнеса</p>
      <div className="grid md:grid-cols-[1fr_1fr] gap-10 md:gap-16">
        <div>
          <h2 id="biz-title" className="font-serif text-[32px] sm:text-[40px] leading-[1.1] font-medium text-ink">
            Магазин и столовая принимают заказы без звонков
          </h2>
          <p className="mt-5 text-[16px] leading-relaxed text-ink-soft max-w-[480px]">
            Покупатель открывает ваш магазин по ссылке, QR-коду или в каталоге, собирает заказ и следит за ним на странице. Вы видите новые заказы на планшете у кассы и выдаёте их с точной суммой.
          </p>
          <div className="mt-7 flex flex-wrap items-center gap-3">
            <Link
              to={authed ? '/cabinet/new' : '/register?returnTo=%2Fcabinet%2Fnew'}
              className="inline-flex items-center gap-2 bg-ink hover:bg-ink/90 !text-cream text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors"
            >
              Открыть магазин на goods
              <Icon name="arrow-right" size={16} />
            </Link>
            <Link
              to="/cabinet"
              className="inline-flex items-center bg-white hover:bg-cream-deep !text-ink border border-line text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors"
            >
              Кабинет
            </Link>
          </div>
        </div>
        <ol className="space-y-5">
          {steps.map((s) => (
            <li key={s.n} className={`flex gap-6 items-baseline border-t border-line pt-5 ${s.offset}`}>
              <span className="font-serif text-2xl text-gold shrink-0 w-10">{s.n}</span>
              <div>
                <h3 className="text-lg font-semibold text-ink">{s.title}</h3>
                <p className="mt-1 text-[15px] text-ink-soft leading-relaxed">{s.text}</p>
              </div>
            </li>
          ))}
        </ol>
      </div>
    </section>
  )
}
