import { Link } from 'react-router-dom'
import { useAuthStore } from '@/store/authStore'
import { Icon } from '@/components/ui/Icon'

const benefits = [
  'Все заказы на одном экране — на планшете у кассы, компьютере или телефоне, со звуком о новом заказе.',
  'Покупатель сам выбирает время получения, а вы заранее видите, что и к какому часу собрать.',
  'Весовой товар — по точной сумме: итог считается по фактическому весу при выдаче.',
  'Покупатель видит статус заказа у себя в телефоне и не звонит спросить, готово ли.',
] as const

const steps = [
  { icon: 'store', title: 'Соберите каталог', text: 'Добавьте категории и товары с фото — поштучно или на вес, с ценой за штуку или за килограмм.' },
  { icon: 'qr-code', title: 'Поделитесь ссылкой или QR', text: 'Повесьте QR-код у кассы, отправьте ссылку постоянным покупателям. Заказ оформляется с телефона на одном экране.' },
  { icon: 'bell', title: 'Принимайте и выдавайте', text: 'Новый заказ приходит на экран со звуком. Примите, соберите и выдайте его с точной суммой.' },
] as const

/**
 * «Для бизнеса» — bottom block of the catalog home (ARCHITECTURE_CYCLE27.md §546).
 * Steps markup is a copy of `#how` in frontend/src/pages/HomePage.tsx (the ezbook sample).
 */
export function BusinessBlock() {
  const authed = useAuthStore((s) => s.isAuthenticated())
  return (
    <section aria-labelledby="biz-title" className="mt-20 md:mt-28 border-t border-line pt-12">
      <p className="text-[13px] font-semibold tracking-[0.14em] uppercase text-gold-dark mb-4">Для бизнеса</p>
      <div className="grid md:grid-cols-[1.05fr_0.95fr] gap-10 md:gap-16 items-start">
        <div>
          <h2 id="biz-title" className="font-serif text-[32px] sm:text-[40px] leading-[1.1] font-medium text-ink">
            Магазин и кафе принимают заказы без звонков и переписок
          </h2>
          <p className="mt-5 text-[16px] leading-relaxed text-ink-soft max-w-[480px]">
            Покупатели заказывают сами — по ссылке, QR-коду на кассе или из каталога goods. Вам больше не нужно принимать заказы по телефону и искать их по чатам: все заказы собраны в одном месте.
          </p>
          <div className="mt-7 flex flex-wrap items-center gap-3">
            <Link
              to={authed ? '/cabinet/new' : '/register?returnTo=%2Fcabinet%2Fnew'}
              className="inline-flex items-center gap-2 bg-ink hover:bg-ink/90 !text-cream text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors"
            >
              Подключить магазин
              <Icon name="arrow-right" size={16} aria-hidden />
            </Link>
            <Link
              to="/cabinet"
              className="inline-flex items-center bg-white hover:bg-cream-deep !text-ink border border-line text-[15px] font-semibold px-7 py-3.5 rounded-full transition-colors"
            >
              Войти в кабинет
            </Link>
          </div>
        </div>
        <ul className="space-y-4 md:pt-2">
          {benefits.map((b) => (
            <li key={b} className="flex items-start gap-3 text-[15px] leading-[1.6] text-ink">
              <Icon name="check" size={18} className="text-gold-dark shrink-0 mt-[3px]" aria-hidden />
              <span>{b}</span>
            </li>
          ))}
        </ul>
      </div>
      <div className="mt-12 md:mt-16 rounded-[28px] bg-cream-deep px-6 py-10 sm:px-10 md:px-12 md:py-14">
        <h3 id="biz-steps-title" className="font-serif text-[26px] sm:text-[28px] leading-[1.2] font-medium text-ink mb-8 md:mb-10">
          Как начать принимать заказы
        </h3>
        <ol aria-labelledby="biz-steps-title" className="list-none grid md:grid-cols-3 gap-10">
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
    </section>
  )
}
