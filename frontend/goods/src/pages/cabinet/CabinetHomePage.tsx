import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { shopsApi } from '../../api/shops'
import { LinkButton } from '../../components/LinkButton'
import { Icon } from '@/components/ui/Icon'
import { EmptyState, ErrorState, LoadingList } from '../../components/StatePanels'
import { getGoodsErrorMessage } from '../../utils/orderError'

/** «Кабинет» — my shops (owner or staff, blocked ones included) plus a pointer to the salons on ezbook (§399.3). */
export function CabinetHomePage() {
  const shops = useQuery({ queryKey: ['my-shops'], queryFn: shopsApi.my })
  const summary = useQuery({ queryKey: ['kinds-summary'], queryFn: shopsApi.kindsSummary, retry: false })

  return (
    <main className="max-w-[860px] mx-auto px-4 sm:px-8 pt-10">
      <div className="flex items-end justify-between gap-4 flex-wrap mb-7">
        <div>
          <h1 className="font-serif text-[32px] text-ink">Мои магазины</h1>
          <p className="text-sm text-ink-soft mt-1">Магазины, которыми вы владеете или где вы работаете сотрудником.</p>
        </div>
        <LinkButton to="/cabinet/new">
          <Icon name="plus" size={16} /> Открыть магазин
        </LinkButton>
      </div>

      {shops.isLoading ? (
        <LoadingList rows={2} />
      ) : shops.isError ? (
        <ErrorState message={getGoodsErrorMessage(shops.error, 'Не удалось загрузить магазины.')} onRetry={() => void shops.refetch()} />
      ) : !shops.data || shops.data.length === 0 ? (
        <EmptyState
          title="У вас пока нет магазинов"
          text="Создайте магазин — это занимает несколько минут: название, город и адрес для покупателей."
          action={<LinkButton to="/cabinet/new">Открыть магазин</LinkButton>}
        />
      ) : (
        <ul className="grid sm:grid-cols-2 gap-4">
          {shops.data.map((s) => (
            <li key={s.id}>
              <Link
                to={`/cabinet/${s.id}/orders`}
                className="flex items-center gap-4 rounded-2xl border border-line bg-white p-5 hover:border-line-strong hover:shadow-soft transition-all !text-ink h-full"
              >
                {s.logoUrl ? (
                  <img src={s.logoUrl} alt="" className="w-14 h-14 rounded-xl object-cover shrink-0" />
                ) : (
                  <span className="w-14 h-14 rounded-xl bg-cream-deep flex items-center justify-center text-gold-dark font-serif text-xl shrink-0">
                    {s.name[0]}
                  </span>
                )}
                <div className="min-w-0">
                  <p className="font-semibold truncate">{s.name}</p>
                  <p className="text-xs text-muted truncate">goods.ezbook.ru/{s.slug}</p>
                  <p className="text-xs mt-1.5 text-ink-soft">
                    {s.myRole === 'Staff' ? 'Вы — сотрудник' : 'Вы — владелец'}
                    {!s.isActive && <span className="ml-2 text-danger font-semibold">Магазин заблокирован администратором</span>}
                  </p>
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}

      {shops.data && shops.data.length > 0 && (
        <nav aria-label="Аккаунт" className="mt-8 flex gap-3 flex-wrap">
          <Link to="/cabinet/devices" className="inline-flex items-center gap-2 min-h-[44px] rounded-full border border-line bg-white px-5 text-sm font-semibold !text-ink hover:border-line-strong">
            <Icon name="bell" size={15} strokeWidth={1.8} /> Устройства и уведомления
          </Link>
          {shops.data.some((s) => s.myRole !== 'Staff') && (
            <Link to="/cabinet/subscription" className="inline-flex items-center gap-2 min-h-[44px] rounded-full border border-line bg-white px-5 text-sm font-semibold !text-ink hover:border-line-strong">
              <Icon name="credit-card" size={15} strokeWidth={1.8} /> Подписка
            </Link>
          )}
        </nav>
      )}

      {summary.data && summary.data.services.count > 0 && (
        <p className="mt-8 text-sm text-ink-soft">
          Ваши салоны — на{' '}
          <a href={summary.data.services.siteUrl} className="text-gold hover:text-gold-dark font-medium">
            ezbook.ru
          </a>
          .
        </p>
      )}
    </main>
  )
}
