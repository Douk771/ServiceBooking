import { useQuery } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import { legalApi } from '../api/legal'
import { Card } from '../components/ui/Card'
import { Button } from '../components/ui/Button'
import { Icon } from '../components/ui/Icon'
import { getLegalErrorMessage } from '../utils/legalError'
import type { LegalDocumentType } from '../types'

interface Props {
  type: LegalDocumentType
}

/**
 * Renders /privacy and /terms. Public, no auth required (US-36 п. 1). Text arrives as an HTML
 * fragment from the server (API_CONTRACT.md §2) — dangerouslySetInnerHTML is safe here because the
 * server is the sole author of App_Data/legal (ARCHITECTURE.md §4.2), the same trust level as
 * appsettings.Production.json.
 */
export function LegalDocumentPage({ type }: Props) {
  const { data, isLoading, isError, error, refetch, isRefetching } = useQuery({
    queryKey: ['legal-document', type],
    queryFn: () => legalApi.getDocument(type),
  })

  if (isLoading) {
    return (
      <div className="max-w-[760px] mx-auto px-8 pt-11 pb-24">
        <div className="h-9 w-2/3 bg-cream-deep rounded-xl animate-pulse mb-6" />
        <div className="flex flex-col gap-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="h-4 bg-cream-deep rounded-lg animate-pulse" />
          ))}
        </div>
      </div>
    )
  }

  if (isError || !data) {
    return (
      <div className="max-w-[760px] mx-auto px-8 pt-11 pb-24">
        <Card className="p-12 text-center text-muted">
          <Icon name="alert-circle" size={32} strokeWidth={1.4} className="mx-auto mb-3" />
          <p className="text-lg font-medium text-ink-soft mb-4">{getLegalErrorMessage(error)}</p>
          <Button variant="secondary" loading={isRefetching} onClick={() => refetch()}>
            Попробовать снова
          </Button>
        </Card>
      </div>
    )
  }

  return (
    <div className="max-w-[760px] mx-auto px-8 pt-11 pb-24">
      {data.isDraft && (
        <div className="mb-6 flex items-start gap-2.5 rounded-2xl bg-warning-bg text-warning px-4 py-3 text-sm">
          <Icon name="alert-circle" size={16} strokeWidth={1.8} className="shrink-0 mt-0.5" />
          <p>
            <strong>Черновая редакция.</strong> Документ подготовлен командой сервиса и ожидает
            юридической проверки; будет заменён без уведомления.
          </p>
        </div>
      )}

      <h1 className="font-serif text-[30px] font-medium text-ink mb-2">{data.title}</h1>
      <p className="text-sm text-ink-soft mb-9">
        Версия {data.version} · действует с {format(parseISO(data.effectiveFrom), 'd MMMM yyyy', { locale: ru })}
      </p>

      <div
        className="legal-content text-[15px] leading-[1.7] text-ink [&_h2]:font-serif [&_h2]:text-xl [&_h2]:font-medium [&_h2]:mt-8 [&_h2]:mb-3 [&_h3]:font-semibold [&_h3]:mt-5 [&_h3]:mb-2 [&_p]:mb-3 [&_ul]:list-disc [&_ul]:pl-5 [&_ul]:mb-3 [&_ol]:list-decimal [&_ol]:pl-5 [&_ol]:mb-3 [&_li]:mb-1 [&_a]:text-gold [&_a]:hover:text-gold-dark [&_table]:w-full [&_table]:my-3 [&_td]:border [&_td]:border-line [&_td]:p-2 [&_th]:border [&_th]:border-line [&_th]:p-2"
        dangerouslySetInnerHTML={{ __html: data.contentHtml }}
      />
    </div>
  )
}
