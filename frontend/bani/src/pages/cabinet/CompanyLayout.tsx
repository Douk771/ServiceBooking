import { useCallback, useEffect } from 'react'
import { Link, NavLink, Navigate, Outlet, useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Icon } from '@/components/ui/Icon'
import { getStayErrorMessage, httpStatus } from '@/utils/slots/slotError'
import { bathsCabinetApi } from '../../api/bathsCabinet'
import { cabinetTabs, businessTodayIn, defaultCabinetTab, roleLabel } from '../../cabinet/cabinetTabs'
import { useBathsCompany, type CabinetOutletContext } from '../../cabinet/cabinetVertical'
import { useBookingsRevision } from '../../cabinet/useBookingsRevision'
import { can } from '@/utils/slots/slotPermissions'
import { ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { NotFoundPage } from '../NotFoundPage'

const COMPANY_POLL_MS = 60_000

/**
 * `/cabinet/:companyId/*` — the shell of one «Бани» company: loads `GET /api/baths/companies/{id}` once, builds the menu from
 * `myPermissions` (never from the role name) and polls the revision of the bookings. MINIMAL: FE-42-5 owns the full shell (checklist,
 * gate and plan banners) and replaces this file; the contract with the screens of FE-42-6 is the outlet context `{ company, refresh }`.
 */
export function CompanyLayout() {
  const { companyId = '' } = useParams()
  const qc = useQueryClient()
  const query = useQuery({
    queryKey: ['baths-company', companyId],
    queryFn: () => bathsCabinetApi.company(companyId),
    refetchInterval: COMPANY_POLL_MS,
    retry: (count, err) => httpStatus(err) === undefined && count < 2,
  })
  const company = query.data
  const refresh = useCallback(() => void qc.invalidateQueries({ queryKey: ['baths-company', companyId] }), [qc, companyId])
  useBookingsRevision(companyId, !!company && can(company.myPermissions, 'ViewBookings'))

  useEffect(() => {
    if (company) document.title = `${company.name} — кабинет · EZBOOK Бани`
    return () => {
      document.title = 'EZBOOK Бани'
    }
  }, [company])

  if (query.isLoading) {
    return (
      <main className="mx-auto max-w-[1180px] px-4 py-8 sm:px-8">
        <LoadingList rows={3} />
      </main>
    )
  }
  if (query.isError && httpStatus(query.error) === 404) return <NotFoundPage title="Компания не найдена" hint="Её нет, или вы не участник этой компании." />
  if (query.isError || !company) {
    return (
      <main className="mx-auto max-w-[760px] px-4 py-10 sm:px-8">
        <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить компанию.')} onRetry={() => void query.refetch()} />
      </main>
    )
  }

  const ctx: CabinetOutletContext = { company, refresh }
  const tabs = cabinetTabs(company.myPermissions)
  const today = businessTodayIn(company.timeZoneId)

  return (
    <div>
      <div className="border-b border-line bg-white/50">
        <div className="mx-auto max-w-[1180px] px-4 pt-6 sm:px-8">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <Link to="/cabinet" className="text-xs font-medium text-ink-soft hover:text-gold-dark">
                ← Мои компании
              </Link>
              <h1 className="mt-1 truncate font-serif text-[28px] leading-tight text-ink sm:text-[34px]">{company.name}</h1>
              <p className="mt-0.5 text-xs text-muted">
                {roleLabel(company.myRole)} · {company.cityName}
                {!company.isActive && <span className="ml-2 font-semibold text-danger">Компания заблокирована администратором</span>}
              </p>
            </div>
            {can(company.myPermissions, 'ViewCabinet') && (
              <a
                href={company.publicUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex min-h-[44px] items-center gap-1.5 rounded-full border border-line bg-white px-4 text-sm font-medium !text-ink hover:border-line-strong"
              >
                <Icon name="external-link" size={14} strokeWidth={1.7} /> Страница для гостей
                <span className="sr-only"> (откроется в новой вкладке)</span>
              </a>
            )}
          </div>
          <nav aria-label="Разделы кабинета" className="-mx-4 mt-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
            <ul className="flex min-w-max gap-1 pb-px">
              {tabs.map((t) => (
                <li key={t.id}>
                  <NavLink
                    to={`/cabinet/${company.id}/${t.id === 'day' ? `service-day/${today}` : t.to}`}
                    className={({ isActive }) =>
                      `relative inline-flex min-h-[44px] items-center gap-2 border-b-2 px-3.5 text-sm font-medium transition-colors ${
                        isActive ? 'border-ink text-ink' : 'border-transparent text-ink-soft hover:text-ink'
                      }`
                    }
                  >
                    {t.label}
                    {t.id === 'orders' && !!company.awaitingPaymentCount && company.awaitingPaymentCount > 0 && (
                      <span
                        className="inline-flex min-w-[20px] items-center justify-center rounded-full bg-gold px-1.5 text-[11px] font-bold leading-5 text-white"
                        aria-label={`ожидают проверки оплаты: ${company.awaitingPaymentCount}`}
                      >
                        {company.awaitingPaymentCount}
                      </span>
                    )}
                  </NavLink>
                </li>
              ))}
            </ul>
          </nav>
        </div>
      </div>
      <Outlet context={ctx} />
    </div>
  )
}

/** `/cabinet/:companyId` itself: «День услуг» for owner and administrator, «Расписание» for a bather. */
export function CompanyIndexRedirect() {
  const { company } = useBathsCompany()
  const tab = defaultCabinetTab(company.myPermissions)
  if (!tab) {
    return <main className="mx-auto max-w-[560px] px-4 py-16 text-center text-sm text-ink-soft">У вас нет доступа ни к одному разделу этой компании. Обратитесь к владельцу.</main>
  }
  return <Navigate to={`/cabinet/${company.id}/${tab === 'day' ? `service-day/${businessTodayIn(company.timeZoneId)}` : cabinetTabs(company.myPermissions).find((t) => t.id === tab)!.to}`} replace />
}
