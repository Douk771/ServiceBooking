import { Link, NavLink, Navigate, Outlet, useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Icon } from '@/components/ui/Icon'
import { useCallback, useEffect } from 'react'
import { staysCompaniesApi } from '../../api/staysCompanies'
import { ErrorState, LoadingList } from '../../components/StatePanels'
import { companyKey, useStaysCompany, type CompanyContext } from '../../hooks/useStaysCompany'
import { can, cabinetTabs, defaultCabinetTab, roleLabel } from '../../utils/permissions'
import { getStayErrorMessage, httpStatus } from '../../utils/stayError'
import { NotFoundPage } from '../NotFoundPage'
import type { StaysCompanyManageDto } from '../../types'

/** Waiting-for-check counter and the plan are refreshed in the background so the badge stays honest while the tab is open. */
const COMPANY_POLL_MS = 30_000

const CHECKLIST_LINKS: Record<StaysCompanyManageDto['checklist'][number]['code'], { to: string; label: string }> = {
  ProfileFilled: { to: 'settings', label: 'Заполнить профиль' },
  PaymentDetails: { to: 'settings', label: 'Указать реквизиты' },
  ProviderInfo: { to: 'settings', label: 'Указать исполнителя' },
  HousePublished: { to: 'houses', label: 'Опубликовать дом' },
  Plan: { to: '/cabinet/subscription', label: 'Выбрать тариф' },
}

/**
 * `/cabinet/:companyId/*` — the shell of one company's cabinet. Loads `GET /api/stays/companies/{id}` once; the menu is built from
 * `myPermissions` (the server's answer), never from the role name. 404 = not a member (and indistinguishable from «no such
 * company»), 403 = a member without the right to see the cabinet.
 */
export function CompanyLayout() {
  const { companyId = '' } = useParams()
  const qc = useQueryClient()
  const query = useQuery({
    queryKey: companyKey(companyId),
    queryFn: () => staysCompaniesApi.get(companyId),
    refetchInterval: COMPANY_POLL_MS,
    retry: (count, err) => httpStatus(err) === undefined && count < 2,
  })
  const company = query.data
  const refresh = useCallback(() => void qc.invalidateQueries({ queryKey: companyKey(companyId) }), [qc, companyId])

  useEffect(() => {
    if (company) document.title = `${company.name} — кабинет · ezbook Дома`
    return () => {
      document.title = 'ezbook · Дома'
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

  const ctx: CompanyContext = { company, refresh }
  const perms = company.myPermissions
  const tabs = cabinetTabs(perms)
  const manage = can(perms, 'ManageCompany')
  const pending = company.checklist.filter((c) => !c.done)

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
            <a
              href={company.publicUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex min-h-[44px] items-center gap-1.5 rounded-full border border-line bg-white px-4 text-sm font-medium !text-ink hover:border-line-strong"
            >
              <Icon name="external-link" size={14} strokeWidth={1.7} /> Страница для гостей
              <span className="sr-only"> (откроется в новой вкладке)</span>
            </a>
          </div>

          <nav aria-label="Разделы кабинета" className="-mx-4 mt-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
            <ul className="flex min-w-max gap-1 pb-px">
              {tabs.map((t) => (
                <li key={t.id}>
                  <NavLink
                    to={`/cabinet/${company.id}/${t.to}`}
                    className={({ isActive }) =>
                      `relative inline-flex min-h-[44px] items-center gap-2 border-b-2 px-3.5 text-sm font-medium transition-colors ${
                        isActive ? 'border-ink text-ink' : 'border-transparent text-ink-soft hover:text-ink'
                      }`
                    }
                  >
                    {t.label}
                    {t.id === 'bookings' && !!company.awaitingPaymentCount && company.awaitingPaymentCount > 0 && (
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

      {manage && (!company.gate.accepting || (company.plan.warningLevel !== 'None' && company.plan.text) || pending.length > 0) && (
        <div className="mx-auto max-w-[1180px] px-4 pt-5 sm:px-8">
          <div className="flex flex-col gap-3">
            {!company.gate.accepting && company.gate.reasonText && (
              <p role="status" className="rounded-2xl bg-warning-bg px-5 py-3 text-sm text-warning" data-testid="gate-banner">
                <span className="font-semibold">Гости пока не могут бронировать.</span> {company.gate.reasonText}
              </p>
            )}
            {company.plan.warningLevel !== 'None' && company.plan.text && (
              <p
                role="status"
                className={`rounded-2xl px-5 py-3 text-sm ${
                  company.plan.warningLevel === 'Expired' || company.plan.warningLevel === 'NoPlan' || company.plan.warningLevel === 'OverLimit'
                    ? 'bg-danger-bg text-danger'
                    : 'bg-warning-bg text-warning'
                }`}
                data-testid="plan-banner"
              >
                {company.plan.text}{' '}
                <Link to="/cabinet/subscription" className="font-semibold underline">
                  Тариф
                </Link>
              </p>
            )}
            {pending.length > 0 && (
              <section aria-label="Что осталось настроить" className="rounded-2xl border border-line bg-white px-5 py-4" data-testid="checklist">
                <p className="mb-2 text-sm font-semibold text-ink">Что осталось настроить</p>
                <ul className="flex flex-col gap-1.5">
                  {pending.map((c) => {
                    const link = CHECKLIST_LINKS[c.code]
                    return (
                      <li key={c.code} className="flex flex-wrap items-center justify-between gap-2 text-sm text-ink-soft">
                        <span>{c.text}</span>
                        <Link to={link.to.startsWith('/') ? link.to : `/cabinet/${company.id}/${link.to}`} className="min-h-[32px] font-semibold text-gold-dark hover:underline">
                          {link.label}
                        </Link>
                      </li>
                    )
                  })}
                </ul>
              </section>
            )}
          </div>
        </div>
      )}

      <Outlet context={ctx} />
    </div>
  )
}

/** `/cabinet/:companyId` itself: the board for owner and manager, the schedule for a housekeeper. */
export function CompanyIndexRedirect() {
  const { company } = useStaysCompany()
  const to = defaultCabinetTab(company.myPermissions)
  if (!to) {
    return (
      <main className="mx-auto max-w-[560px] px-4 py-16 text-center text-sm text-ink-soft">
        У вас нет доступа ни к одному разделу этой компании. Обратитесь к владельцу.
      </main>
    )
  }
  return <Navigate to={`/cabinet/${company.id}/${to}`} replace />
}
