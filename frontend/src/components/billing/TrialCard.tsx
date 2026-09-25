import { format, parseISO } from 'date-fns'
import { Card } from '../ui/Card'
import { formatDaysLeft } from '../../utils/pricingFormat'
import type { TrialStateDto } from '../../api/billing'

interface Props {
  trial: TrialStateDto
}

/**
 * "Пробный период" card on `/billing` (API_CONTRACT_CYCLE18.md §371 п.9, Т2). Rendered whenever
 * `trial.state` is `'Active'` or `'Expired'` — never `null`, per contract (`trial` stays a full
 * object once ever granted, so this card can keep showing the end date even after expiry).
 *
 * 🔴 This card has NO dismiss button, by design (Т2): the end date must be visible permanently, not
 * only inside a closeable reminder banner. `TrialBanner` is the closeable one; this is not.
 */
export function TrialCard({ trial }: Props) {
  const endsAt = trial.endsAt ? format(parseISO(trial.endsAt), 'dd.MM.yyyy') : null
  const isActive = trial.state === 'Active'

  return (
    <Card className="p-[26px] mb-6 border border-line">
      <div className="flex items-center justify-between flex-wrap gap-2 mb-3">
        <h2 className="text-[15.5px] font-semibold text-ink">Пробный период</h2>
        <span
          className={`text-xs font-semibold px-2.5 py-1 rounded-full ${
            isActive ? 'bg-[#EAF4EC] text-[#2F7A45]' : 'bg-cream-deep text-muted'
          }`}
        >
          {isActive ? 'Активен' : 'Завершён'}
        </span>
      </div>

      {endsAt && (
        <p className="text-sm text-ink-soft mb-1">
          {isActive ? 'Действует до ' : 'Действовал до '}
          <strong className="text-ink">{endsAt}</strong>
          {typeof trial.daysLeft === 'number' && isActive && ` — осталось ${formatDaysLeft(trial.daysLeft)}`}
        </p>
      )}

      {/* §371: печатается дословно, сервер решил можно ли закрыть (dismissible на warning, а не здесь). */}
      <p className="text-sm text-ink-soft">{trial.message}</p>

      {(trial.includes ?? []).length > 0 && (
        <ul className="mt-3 text-xs text-ink-soft list-disc list-inside grid gap-1">
          {(trial.includes ?? []).map((i) => (
            <li key={i}>{i}</li>
          ))}
        </ul>
      )}
    </Card>
  )
}
