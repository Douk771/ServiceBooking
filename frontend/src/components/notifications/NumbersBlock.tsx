import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { NUMBERS_OVERVIEW_QUERY_KEY, notificationNumbersApi, type TransportNumbersDto } from '../../api/notificationNumbers'
import { Card } from '../ui/Card'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import { ConnectWizard } from './ConnectWizard'
import { NumberRow } from './NumberRow'
import * as T from './numbersTexts'

/** Блок «Номера» (US-04/05): общие номера аккаунта для всех компаний. Всё содержимое — поля GET /notification-channels/overview. */
export function NumbersBlock({ className = '' }: { className?: string }) {
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: NUMBERS_OVERVIEW_QUERY_KEY,
    queryFn: notificationNumbersApi.overview,
  })
  const [wizardFor, setWizardFor] = useState<TransportNumbersDto['transport'] | null>(null)

  if (isLoading) {
    return <div role="status" aria-label="Загрузка номеров" className={`h-40 bg-cream-deep rounded-2xl animate-pulse ${className}`} />
  }
  if (isError || !data) {
    return (
      <Card className={`p-8 text-center text-muted ${className}`}>
        <Icon name="alert-circle" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
        <p className="mb-3">{T.NUMBERS_LOAD_ERROR}</p>
        <Button size="sm" variant="secondary" onClick={() => void refetch()}>Повторить</Button>
      </Card>
    )
  }

  // Мастер берёт транспорт из свежего overview, чтобы wizardStep не устаревал.
  const active = wizardFor ? data.transports.find((t) => t.transport === wizardFor) : undefined

  return (
    <Card className={className}>
      <div className="px-5 pt-5 pb-3 border-b border-line">
        <h2 className="text-base font-semibold text-ink">{T.NUMBERS_TITLE}</h2>
        <p className="text-sm text-ink-soft mt-0.5">{data.note}</p>
        <p className="text-xs text-muted mt-1">{data.companiesText}</p>
        {data.messagingDisabledText && <p className="text-sm text-warning bg-warning-bg rounded-xl px-3 py-2 mt-3">{data.messagingDisabledText}</p>}
        <p className="text-xs text-muted mt-2">{data.statusNotice}</p>
      </div>

      {data.transports.length === 0 ? (
        <p className="px-5 py-8 text-center text-sm text-muted">{T.NUMBERS_EMPTY_TEXT}</p>
      ) : (
        <div className="divide-y divide-line">
          {data.transports.map((t) => (
            <div key={t.transport} className="divide-y divide-line">
              <NumberRow transport={t} onOpenWizard={(x) => setWizardFor(x.transport)} />
              {t.extraChannels.map((ch) => (
                <NumberRow key={ch.id} transport={t} extraChannel={ch} onOpenWizard={() => undefined} />
              ))}
            </div>
          ))}
        </div>
      )}

      {active && <ConnectWizard transport={active} overview={data} onClose={() => setWizardFor(null)} />}
    </Card>
  )
}
