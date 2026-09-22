import { useQuery } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { clientConsentsApi } from '../../api/clientConsents'
import { Icon } from '../ui/Icon'

/**
 * API_CONTRACT_CYCLE5.md §44.1; ARCHITECTURE_CYCLE5.md T5-F6 — "состояние «клиент согласие дал»":
 * a passive status line so staff can see consent was already recorded without having to attempt an
 * upload first. The actual gate before a first upload is reactive (`NotePhotoUploader`); this is the
 * read-only complement to it.
 */
export function PhotoConsentBadge({ companyId, clientKey }: { companyId: string; clientKey: string }) {
  const { data, isLoading } = useQuery({
    queryKey: ['photo-consent', companyId, clientKey],
    queryFn: () => clientConsentsApi.getPhotoConsent(companyId, clientKey),
  })

  if (isLoading || !data) return null

  // `granted: false` and a stale text version are equivalent for the frontend — both mean "the form
  // is needed again" (§44.1).
  const needsForm = !data.granted || data.textVersionOutdated

  if (needsForm) {
    return (
      <p className="text-xs text-muted flex items-center gap-1">
        <Icon name="image" size={12} strokeWidth={1.8} /> Фото: согласие клиента не получено
      </p>
    )
  }

  return (
    <p className="text-xs text-success flex items-center gap-1">
      <Icon name="check" size={12} strokeWidth={2} /> Фото: клиент согласие дал
      {data.grantedAt && ` (${format(parseISO(data.grantedAt), 'd MMM yyyy', { locale: ru })})`}
    </p>
  )
}
