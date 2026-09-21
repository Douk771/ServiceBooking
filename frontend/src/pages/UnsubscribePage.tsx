import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { useQuery, useMutation } from '@tanstack/react-query'
import { AxiosError } from 'axios'
import { notificationsApi } from '../api/notifications'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Icon } from '../components/ui/Icon'

/**
 * US-33 п. 4 — the link every message carries. Anonymous, keyed by an unguessable token
 * (API_CONTRACT_CYCLE4.md §32.2). A bad signature is 404, not 400, deliberately: the contract says
 * enumeration shouldn't be distinguishable from "wrong link".
 */
export function UnsubscribePage() {
  const { token = '' } = useParams<{ token: string }>()
  const [done, setDone] = useState(false)

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['unsubscribe-info', token],
    queryFn: () => notificationsApi.getUnsubscribeInfo(token),
    enabled: !!token,
    retry: false,
  })

  const mut = useMutation({
    mutationFn: () => notificationsApi.unsubscribe(token),
    onSuccess: () => setDone(true),
  })

  const notFound = isError && (error as AxiosError)?.response?.status === 404

  return (
    <div className="max-w-md mx-auto px-6 pt-16 pb-24 text-center">
      {isLoading ? (
        <div className="h-40 bg-cream-deep rounded-2xl animate-pulse" />
      ) : notFound || !data ? (
        <Card className="p-10">
          <Icon name="alert-circle" size={32} strokeWidth={1.5} className="mx-auto mb-3 text-muted" />
          <p className="text-ink-soft">Ссылка недействительна или устарела.</p>
        </Card>
      ) : (
        <Card className="p-10">
          {data.alreadyOptedOut || done ? (
            <>
              <div className="w-14 h-14 rounded-full bg-success-bg flex items-center justify-center mx-auto mb-4">
                <Icon name="check" size={26} strokeWidth={1.8} className="text-success" />
              </div>
              <h1 className="font-serif text-xl font-medium text-ink mb-2">Вы отписаны</h1>
              <p className="text-sm text-ink-soft">
                Сообщения на номер {data.phoneMasked} больше не будут приходить ни от одного салона на платформе.
              </p>
            </>
          ) : (
            <>
              <h1 className="font-serif text-xl font-medium text-ink mb-2">Отказаться от уведомлений?</h1>
              <p className="text-sm text-ink-soft mb-6">
                Номер {data.phoneMasked} перестанет получать сообщения о записях в WhatsApp от всех салонов на
                платформе.
              </p>
              {mut.isError && <p className="text-sm text-danger mb-3">Не удалось выполнить действие. Попробуйте ещё раз.</p>}
              <Button variant="danger" loading={mut.isPending} onClick={() => mut.mutate()}>
                Отказаться от уведомлений
              </Button>
            </>
          )}
        </Card>
      )}
    </div>
  )
}
