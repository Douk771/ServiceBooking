import { useCallback, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { staysHousesApi } from '../../api/staysHouses'
import { HouseContentCard } from '../../components/houses/HouseContentCard'
import { HouseSetupCard } from '../../components/houses/HouseSetupCard'
import { HouseTabProvider } from '../../components/houses/houseContext'
import { houseKey } from '../../components/houses/houseKey'
import { PhotosTab } from '../../components/houses/PhotosTab'
import { PricesTab } from '../../components/houses/PricesTab'
import { RegistryTab } from '../../components/houses/RegistryTab'
import { QrDialog } from '../../components/cabinet/QrDialog'
import { ErrorState, LoadingList } from '../../components/StatePanels'
import { useStaysCompany } from '../../hooks/useStaysCompany'
import type { HouseManageDto } from '../../types'
import { can } from '../../utils/permissions'
import { getStayErrorMessage, isNotFound } from '../../utils/stayError'
import { NotFoundPage } from '../NotFoundPage'

const TABS = [
  { id: 'desc', label: 'Описание' },
  { id: 'photos', label: 'Фото' },
  { id: 'prices', label: 'Цены' },
  { id: 'registry', label: 'Реестр и публикация' },
  { id: 'checkin', label: 'Заселение' },
] as const
type TabId = (typeof TABS)[number]['id']

const isTab = (v: string | null): v is TabId => TABS.some((t) => t.id === v)

/**
 * `/cabinet/:companyId/houses/:houseId` — one house in five tabs (§37.14.5): «Описание», «Фото», «Цены», «Реестр и публикация», «Заселение».
 * The tab is in `?tab=` so a link and a refresh keep the place. Owner edits everything; a manager (`EditHouseContent`) edits the description,
 * photos and the check-in text and sees the rest read-only or not at all.
 */
export function HouseEditPage() {
  const { houseId = '' } = useParams()
  const { company } = useStaysCompany()
  const qc = useQueryClient()
  const [sp, setSp] = useSearchParams()
  const [qr, setQr] = useState(false)
  const canManage = can(company.myPermissions, 'ManageHouses')
  const canEditContent = can(company.myPermissions, 'EditHouseContent')
  const key = houseKey(company.id, houseId)

  const query = useQuery({
    queryKey: key,
    queryFn: () => staysHousesApi.get(company.id, houseId),
    retry: (count, err) => !isNotFound(err) && count < 1,
  })
  const house = query.data
  const setHouse = useCallback(
    (h: HouseManageDto) => {
      qc.setQueryData(key, h)
      void qc.invalidateQueries({ queryKey: ['stays-houses', company.id] })
      void qc.invalidateQueries({ queryKey: ['stays-house'] }) // the public page of this house
    },
    [qc, key, company.id],
  )

  if (!canManage && !canEditContent) return <NotFoundPage title="Раздел недоступен" />
  if (query.isLoading) {
    return (
      <main className="mx-auto max-w-[900px] px-4 pt-8 sm:px-8">
        <LoadingList rows={3} />
      </main>
    )
  }
  if (query.isError && isNotFound(query.error)) return <NotFoundPage title="Дом не найден" hint="Его нет в этой компании." />
  if (query.isError || !house) {
    return (
      <main className="mx-auto max-w-[760px] px-4 pt-8 sm:px-8">
        <ErrorState message={getStayErrorMessage(query.error, 'Не удалось загрузить дом.')} onRetry={() => void query.refetch()} />
      </main>
    )
  }

  const tabParam = sp.get('tab')
  const tab: TabId = isTab(tabParam) ? tabParam : 'desc'
  const status = house.isArchived ? 'В архиве' : house.isPublished ? 'Опубликован' : 'Не опубликован'

  return (
    <HouseTabProvider value={{ companyId: company.id, house, setHouse, canManage, canEditContent, timeZoneId: company.timeZoneId }}>
      <main className="mx-auto max-w-[900px] px-4 pb-6 pt-6 sm:px-8">
        <Link to={`/cabinet/${company.id}/houses`} className="text-xs font-medium text-ink-soft hover:text-gold-dark">
          ← Все дома
        </Link>
        <div className="mt-1 flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 className="truncate font-serif text-[28px] leading-tight text-ink">{house.name}</h2>
            <p className="mt-0.5 text-xs text-muted">
              {status}
              {house.isPublished && (
                <>
                  {' · '}
                  <a href={house.publicUrl} target="_blank" rel="noopener noreferrer" className="text-gold-dark underline">
                    страница для гостей<span className="sr-only"> (откроется в новой вкладке)</span>
                  </a>
                </>
              )}
            </p>
          </div>
          <button
            type="button"
            onClick={() => setQr(true)}
            className="inline-flex min-h-[44px] items-center rounded-full border border-line bg-white px-4 text-sm font-medium text-ink hover:border-line-strong"
          >
            QR-код дома
          </button>
        </div>

        <div role="tablist" aria-label="Разделы дома" className="-mx-4 mt-4 flex gap-1 overflow-x-auto px-4 sm:mx-0 sm:px-0">
          {TABS.map((t) => (
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

        <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} className="flex flex-col gap-6">
          {tab === 'desc' && (
            <>
              <HouseSetupCard key={`setup-${house.id}`} />
              <HouseContentCard key={`content-${house.id}`} part="description" />
            </>
          )}
          {tab === 'photos' && <PhotosTab />}
          {tab === 'prices' && <PricesTab />}
          {tab === 'registry' && <RegistryTab key={`registry-${house.id}`} />}
          {tab === 'checkin' && <HouseContentCard key={`checkin-${house.id}`} part="checkin" />}
        </div>

        {qr && <QrDialog title={house.name} fileName={`${company.slug}-${house.slug}.png`} load={() => staysHousesApi.qr(company.id, house.id)} onClose={() => setQr(false)} />}
      </main>
    </HouseTabProvider>
  )
}
