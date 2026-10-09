import { useCallback } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ServiceContentTab } from '@/components/slots/services/cabinet/ServiceContentTab'
import { ServiceItemsTab } from '@/components/slots/services/cabinet/ServiceItemsTab'
import { ServicePricesTab } from '@/components/slots/services/cabinet/ServicePricesTab'
import { ServiceRulesTab } from '@/components/slots/services/cabinet/ServiceRulesTab'
import { ServiceScheduleTab } from '@/components/slots/services/cabinet/ServiceScheduleTab'
import { ServiceTabProvider } from '@/components/slots/services/cabinet/serviceContext'
import { ErrorState, LoadingList } from '@/components/slots/ui/StatePanels'
import { useCabinetCompany, useSlotVertical } from '@/components/slots/SlotVerticalContext'
import type { ServiceManageDto } from '@/types/slots'
import { can } from '@/utils/slots/slotPermissions'
import { getStayErrorMessage, isNotFound } from '@/utils/slots/slotError'

type TabId = 'desc' | 'schedule' | 'prices' | 'items' | 'rules'

/**
 * `/cabinet/:companyId/services/:serviceId` — one service in five tabs (§39.14.1): «Описание», «Расписание», «Цены», «Позиции», «Правила».
 * The tab is in `?tab=`. The owner edits everything; a manager sees the tabs his rights open (description and photos —
 * `EditServiceContent`; manual dates — `ManageServiceDates`).
 */
export function CabinetServiceEditView() {
  const { api, paths, NotFound } = useSlotVertical()
  const staysServicesApi = api.cabinet
  const { serviceId = '' } = useParams()
  const { company } = useCabinetCompany()
  const qc = useQueryClient()
  const [sp, setSp] = useSearchParams()
  const perms = company.myPermissions
  const canManage = can(perms, 'ManageServices')
  const canEditContent = can(perms, 'EditServiceContent')
  const canManageDates = can(perms, 'ManageServiceDates')
  const key = ['stays-service', company.id, serviceId]

  const query = useQuery({
    queryKey: key,
    queryFn: () => staysServicesApi.get(company.id, serviceId),
    retry: (count, err) => !isNotFound(err) && count < 1,
    enabled: canManage || canEditContent || canManageDates,
  })
  const service = query.data
  const setService = useCallback(
    (s: ServiceManageDto) => {
      qc.setQueryData(key, s)
      void qc.invalidateQueries({ queryKey: ['stays-services', company.id] })
      void qc.invalidateQueries({ queryKey: ['stays-service-page'] }) // the public page of this service
    },
    [qc, company.id, serviceId], // eslint-disable-line react-hooks/exhaustive-deps
  )

  if (!canManage && !canEditContent && !canManageDates) return <NotFound title="Раздел недоступен" />
  if (query.isLoading) {
    return (
      <main className="mx-auto max-w-[900px] px-4 pt-8 sm:px-8">
        <LoadingList rows={3} />
      </main>
    )
  }
  if (query.isError && isNotFound(query.error)) return <NotFound title="Услуга не найдена" hint="Её нет в этой компании." />
  if (query.isError || !service) {
    return (
      <main className="mx-auto max-w-[760px] px-4 pt-8 sm:px-8">
        <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить услугу.')} onRetry={() => void query.refetch()} />
      </main>
    )
  }

  const tabs: { id: TabId; label: string }[] = [
    ...(canEditContent ? [{ id: 'desc' as const, label: 'Описание' }] : []),
    ...(canManage || canManageDates ? [{ id: 'schedule' as const, label: 'Расписание' }] : []),
    ...(canManage ? [{ id: 'prices' as const, label: 'Цены' }, { id: 'items' as const, label: 'Позиции' }, { id: 'rules' as const, label: 'Правила' }] : []),
  ]
  const param = sp.get('tab')
  const tab = tabs.find((t) => t.id === param)?.id ?? tabs[0]?.id
  const status = service.isArchived ? 'В архиве' : service.isPublished ? 'Опубликована' : 'Не опубликована'

  return (
    <ServiceTabProvider value={{ companyId: company.id, service, setService, canManage, canEditContent, canManageDates }}>
      <main className="mx-auto max-w-[900px] px-4 pb-6 pt-6 sm:px-8">
        <Link to={paths.cabinetServices(company.id)} className="text-xs font-medium text-ink-soft hover:text-gold-dark">
          ← Все услуги
        </Link>
        <div className="mt-1">
          <h2 className="truncate font-serif text-[28px] leading-tight text-ink">{service.name}</h2>
          <p className="mt-0.5 text-xs text-muted">
            {status}
            {service.isPublished && (
              <>
                {' · '}
                <a href={service.publicUrl} target="_blank" rel="noopener noreferrer" className="text-gold-dark underline">
                  страница для гостей<span className="sr-only"> (откроется в новой вкладке)</span>
                </a>
              </>
            )}
          </p>
        </div>

        <div role="tablist" aria-label="Разделы услуги" className="-mx-4 mt-4 flex gap-1 overflow-x-auto px-4 sm:mx-0 sm:px-0">
          {tabs.map((t) => (
            <button
              key={t.id}
              role="tab"
              id={`tab-${t.id}`}
              aria-selected={tab === t.id}
              aria-controls={`panel-${t.id}`}
              type="button"
              onClick={() => setSp({ tab: t.id }, { replace: true })}
              className={`min-h-[44px] shrink-0 border-b-2 px-3.5 text-sm font-medium transition-colors ${tab === t.id ? 'border-ink text-ink' : 'border-transparent text-ink-soft hover:text-ink'}`}
            >
              {t.label}
            </button>
          ))}
        </div>
        <div className="border-t border-line pt-6" />

        {tab && (
          <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} className="flex flex-col gap-6">
            {tab === 'desc' && <ServiceContentTab />}
            {tab === 'schedule' && <ServiceScheduleTab />}
            {tab === 'prices' && <ServicePricesTab />}
            {tab === 'items' && <ServiceItemsTab />}
            {tab === 'rules' && <ServiceRulesTab />}
          </div>
        )}
      </main>
    </ServiceTabProvider>
  )
}
