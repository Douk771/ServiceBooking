import { useCallback } from 'react'
import { NavLink, Outlet, useLocation, useParams, Navigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { shopsApi } from '../../api/shops'
import { ErrorState, Skeleton } from '../../components/StatePanels'
import { NotFoundPage } from '../NotFoundPage'
import { getGoodsErrorMessage, httpStatus } from '../../utils/orderError'
import type { ShopContext } from '../../hooks/useShop'

// The route error boundary is keyed by pathname (GoodsApp), so this layout remounts on every tab click; without this the
// tab strip would jump back to the far left each time. Module-level: survives the remount.
let tabsScrollLeft = 0

const OWNER_ONLY_SEGMENTS = ['settings', 'staff', 'hours', 'notifications', 'summary']

/**
 * Shell of `/cabinet/:shopId/*`: loads the shop once, shows the tab bar and hands `{ shop, isOwner }` to the
 * screens through the router outlet. Staff never see the owner tabs, and a typed-in owner URL bounces to
 * orders (the API answers 403 anyway — §392.2).
 */
export function ShopLayout() {
  const tabsRef = useCallback((el: HTMLElement | null) => {
    if (el) el.scrollLeft = tabsScrollLeft
  }, [])
  const { shopId = '' } = useParams<{ shopId: string }>()
  const location = useLocation()
  const { data: shop, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['shop', shopId],
    queryFn: () => shopsApi.get(shopId),
    retry: (count, err) => httpStatus(err) === undefined && count < 2,
  })

  if (isLoading)
    return (
      <div className="max-w-[1180px] mx-auto px-4 sm:px-8 pt-8">
        <Skeleton className="h-16 mb-6" />
        <Skeleton className="h-64" />
      </div>
    )

  if (isError || !shop) {
    const status = httpStatus(error)
    if (status === 404) return <NotFoundPage title="Магазин не найден" hint="Возможно, он удалён, или ссылка неверна." />
    if (status === 403)
      return (
        <main className="max-w-[560px] mx-auto px-4 py-20 text-center">
          <h1 className="font-serif text-2xl text-ink">Нет доступа к магазину</h1>
          <p className="mt-2 text-sm text-ink-soft">Вы не владелец и не сотрудник этого магазина.</p>
        </main>
      )
    return (
      <div className="max-w-[1180px] mx-auto px-4 sm:px-8 pt-8">
        <ErrorState message={getGoodsErrorMessage(error, 'Не удалось загрузить магазин.')} onRetry={() => void refetch()} />
      </div>
    )
  }

  const isOwner = shop.myRole !== 'Staff'
  const segment = location.pathname.split('/')[3] ?? ''
  if (!isOwner && OWNER_ONLY_SEGMENTS.includes(segment)) return <Navigate to={`/cabinet/${shopId}/orders`} replace />

  const tabs = [
    { to: `/cabinet/${shopId}/orders`, label: 'Заказы', show: true },
    { to: `/cabinet/${shopId}/history`, label: 'История', show: true },
    { to: `/cabinet/${shopId}/picklist`, label: 'Лист сборки', show: true },
    { to: `/cabinet/${shopId}/summary`, label: 'Сводка', show: isOwner },
    { to: `/cabinet/${shopId}/catalog`, label: 'Каталог', show: true },
    { to: `/cabinet/${shopId}/menu`, label: 'Меню на дату', show: true },
    { to: `/cabinet/${shopId}/hours`, label: 'Часы работы', show: isOwner },
    { to: `/cabinet/${shopId}/notifications`, label: 'Уведомления', show: isOwner },
    { to: `/cabinet/${shopId}/settings`, label: 'Настройки', show: isOwner },
    { to: `/cabinet/${shopId}/staff`, label: 'Сотрудники', show: isOwner },
    { to: `/cabinet/${shopId}/link`, label: 'Ссылка и QR', show: true },
  ].filter((t) => t.show)

  const ctx: ShopContext = { shop, isOwner }

  return (
    <div>
      <div className="no-print border-b border-line bg-white/50">
        <div className="max-w-[1180px] mx-auto px-4 sm:px-8 pt-6">
          <div className="flex items-center gap-3 mb-4">
            <h1 className="font-serif text-[26px] text-ink leading-tight truncate">{shop.name}</h1>
            <span className="text-xs font-semibold px-2.5 py-1 rounded-full bg-cream-deep text-ink-soft shrink-0">
              {isOwner ? 'Владелец' : 'Сотрудник'}
            </span>
          </div>
          <nav ref={tabsRef} onScroll={(e) => { tabsScrollLeft = e.currentTarget.scrollLeft }} aria-label="Разделы магазина" className="flex gap-1 overflow-x-auto -mb-px">
            {tabs.map((t) => (
              <NavLink
                key={t.to}
                to={t.to}
                className={({ isActive }) =>
                  `px-4 py-3 text-sm font-semibold whitespace-nowrap border-b-2 transition-colors !text-ink ${
                    isActive ? 'border-gold' : 'border-transparent !text-ink-soft hover:!text-ink'
                  }`
                }
              >
                {t.label}
              </NavLink>
            ))}
          </nav>
        </div>
      </div>

      {!shop.isActive && (
        <div role="alert" className="bg-danger-bg text-danger text-sm font-medium text-center px-4 py-3">
          Магазин заблокирован администратором: страница магазина недоступна покупателям, заказы не принимаются.
        </div>
      )}

      <Outlet context={ctx} />
    </div>
  )
}
