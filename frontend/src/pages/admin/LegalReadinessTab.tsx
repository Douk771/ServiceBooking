import { useQuery } from '@tanstack/react-query'
import { adminLegalApi } from '../../api/platformSettings'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Icon } from '../../components/ui/Icon'
import type { LegalBlockerKind } from '../../types'

// API_CONTRACT_CYCLE11.md §116, §119 п. 2 — read-only, SuperAdmin. Diagnostic, not content:
// no client-side caching beyond react-query's default, and the disclaimer is always shown as a
// visible line, never hidden behind an icon or tooltip.

const BLOCKER_LABEL: Record<LegalBlockerKind, string> = {
  UnresolvedPlaceholders: 'В текстах остались незаполненные плейсхолдеры {{…}}',
  MissingValues: 'Файл значений реквизитов не найден или заполнен не полностью',
  DraftDocuments: 'Есть документы или тексты в статусе черновика',
  BrokenLinks: 'Внутренняя ссылка ведёт на несуществующую страницу',
  MissingAnchors: 'Потерян якорь, на который ссылается редирект',
  ArtifactDrift: 'Артефакт разошёлся с исходниками (legal-drafts)',
  LegalUnavailable: 'Снимок правовых документов не загружен',
}

export function LegalReadinessTab() {
  const { data, isLoading, isError, error, refetch, isRefetching } = useQuery({
    queryKey: ['admin-legal-readiness'],
    queryFn: adminLegalApi.getReadiness,
  })

  if (isLoading) {
    return (
      <div className="grid gap-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <div key={i} className="h-20 bg-cream-deep rounded-2xl animate-pulse" />
        ))}
      </div>
    )
  }

  if (isError || !data) {
    // §116 — 503 arrives with a body (ready:false + LegalUnavailable), but the request itself can
    // also fail outright (network, 401/403 session expiry) — treated the same way here: nothing to
    // read, offer a retry.
    const status = (error as { response?: { status?: number } })?.response?.status
    return (
      <Card className="p-12 text-center text-muted">
        <Icon name="alert-circle" size={32} strokeWidth={1.4} className="mx-auto mb-3" />
        <p className="text-lg font-medium text-ink-soft mb-4">
          {status === 403
            ? 'Недостаточно прав для просмотра готовности правовых документов.'
            : 'Не удалось загрузить отчёт готовности.'}
        </p>
        <Button variant="secondary" loading={isRefetching} onClick={() => refetch()}>
          Попробовать снова
        </Button>
      </Card>
    )
  }

  // The endpoint nests placeholders under each document/uiText rather than sending a deduplicated
  // top-level summary (see the deviation note on the LegalReadiness type) — aggregated here for
  // display, by name, across the whole set.
  const placeholdersByName = new Map<string, number>()
  for (const p of [...data.documents.flatMap((d) => d.placeholders), ...data.uiTexts.flatMap((t) => t.placeholders)]) {
    placeholdersByName.set(p.name, (placeholdersByName.get(p.name) ?? 0) + p.count)
  }
  const placeholderSummary = [...placeholdersByName.entries()].sort((a, b) => a[0].localeCompare(b[0]))
  const totalPlaceholderOccurrences = placeholderSummary.reduce((sum, [, count]) => sum + count, 0)
  const totalReAcceptance = data.impact.reduce((sum, r) => sum + r.users, 0)

  return (
    <div>
      <Card className={`p-6 mb-5 ${data.ready ? 'bg-success-bg' : 'bg-warning-bg'}`}>
        <div className="flex items-center gap-2 mb-1">
          <Icon name={data.ready ? 'check-circle' : 'alert-circle'} size={20} strokeWidth={1.8} />
          <h2 className="text-base font-semibold text-ink">
            {data.ready ? 'Комплект готов к публикации' : 'Комплект не готов к публикации'}
          </h2>
        </div>
        <p className="text-xs text-muted">Снято {new Date(data.generatedAtUtc).toLocaleString('ru-RU')}</p>
      </Card>

      {/* Disclaimer is mandatory and shown as a visible line even when ready=true — it is NOT a
          substitute for legal review, only for the mechanical checks (API_CONTRACT_CYCLE11.md §116). */}
      <Card className="p-4 mb-5 text-sm text-ink-soft">{data.disclaimer}</Card>

      {data.blockers.length > 0 && (
        <Card className="p-6 mb-5">
          <h3 className="text-sm font-semibold text-ink mb-3">Что блокирует публикацию</h3>
          <ul className="flex flex-col gap-2">
            {data.blockers.map((b, i) => (
              <li key={i} className="text-sm text-ink-soft flex items-start gap-2">
                <Icon name="alert-circle" size={14} strokeWidth={1.8} className="shrink-0 mt-0.5 text-warning" />
                <span>
                  <strong>{BLOCKER_LABEL[b.kind] ?? b.kind}.</strong> {b.detail}
                </span>
              </li>
            ))}
          </ul>
        </Card>
      )}

      <div className="grid sm:grid-cols-2 gap-3 mb-5">
        <Card className="p-4 text-center">
          <p className="text-xl font-bold text-ink">{totalPlaceholderOccurrences}</p>
          <p className="text-[11px] text-muted mt-0.5">незакрытых плейсхолдеров</p>
        </Card>
        <Card className="p-4 text-center">
          <p className="text-xl font-bold text-ink">{totalReAcceptance}</p>
          <p className="text-[11px] text-muted mt-0.5">получат требование повторного акцепта</p>
        </Card>
      </div>

      {placeholderSummary.length > 0 && (
        <Card className="p-6 mb-5">
          <h3 className="text-sm font-semibold text-ink mb-3">Плейсхолдеры</h3>
          <div className="grid gap-2">
            {placeholderSummary.map(([name, count]) => (
              <div key={name} className="flex items-center justify-between gap-3 text-sm">
                <code className="text-xs bg-cream-deep px-1.5 py-0.5 rounded">{name}</code>
                <span className="text-muted">× {count}</span>
              </div>
            ))}
          </div>
        </Card>
      )}

      {data.impact.length > 0 && (
        <Card className="p-6 mb-5">
          <h3 className="text-sm font-semibold text-ink mb-1">Кому потребуется повторный акцепт</h3>
          <p className="text-xs text-muted mb-3">
            Документы с gate=None никого не блокируют и в этот список не входят.
          </p>
          <div className="grid gap-2">
            {data.impact.map((r) => (
              <div key={r.documentType} className="flex items-center justify-between text-sm">
                <span className="text-ink-soft">
                  {r.documentType} <span className="text-xs text-muted">({r.gate})</span>
                </span>
                <span className="font-medium text-ink">{r.users}</span>
              </div>
            ))}
          </div>
        </Card>
      )}

      <div className="flex justify-end">
        <Button variant="secondary" size="sm" loading={isRefetching} onClick={() => refetch()}>
          Обновить
        </Button>
      </div>
    </div>
  )
}
