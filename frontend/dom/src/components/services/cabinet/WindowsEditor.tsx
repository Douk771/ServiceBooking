import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import type { WindowInput } from '../../../types'
import { MINUTES_PER_DAY, clockOf } from '../../../utils/businessClock'
import { WINDOW_ERROR_TEXT, validateWindows } from '../../../utils/serviceWindows'

const START_OPTIONS = Array.from({ length: 47 }, (_, i) => 360 + i * 30) // 06:00 … 05:30 of the next calendar day
const END_OPTIONS = Array.from({ length: 47 }, (_, i) => 390 + i * 30) // 06:30 … 06:00 of the next calendar day

/** «02:00 (след. дня)»: a time after midnight belongs to the NEXT calendar day, said in words in the list itself. */
const optionLabel = (m: number) => `${clockOf(m)}${m >= MINUTES_PER_DAY ? ' (след. дня)' : ''}`

/**
 * Windows of one day (≤ 3), minutes of the business day (the day runs 06:00 → 06:00). The lists carry «(след. дня)» for the hours
 * after midnight, so «02:00» can never be read as the early morning of the same day. The checks are `validateWindows` (vectors
 * `windows`); the server repeats them and its text wins.
 */
export function WindowsEditor({
  idPrefix,
  windows,
  onChange,
  disabled,
}: {
  idPrefix: string
  windows: readonly WindowInput[]
  onChange: (next: WindowInput[]) => void
  disabled?: boolean
}) {
  const check = validateWindows(windows)
  const set = (i: number, patch: Partial<WindowInput>) => onChange(windows.map((w, j) => (j === i ? { ...w, ...patch } : w)))
  const error = !check.ok
    ? check.error === 'WindowsOverlap' && check.first != null && check.second != null
      ? `Окна ${optionLabel(windows[check.first].startMinute)} и ${optionLabel(windows[check.second].startMinute)} пересекаются`
      : WINDOW_ERROR_TEXT[check.error]
    : null

  return (
    <div className="flex flex-col gap-2">
      {windows.length === 0 && <p className="text-sm text-ink-soft">Закрыто — окон нет</p>}
      {windows.map((w, i) => (
        <div key={i} className="flex flex-wrap items-end gap-2">
          <div className="flex flex-col gap-1">
            <label htmlFor={`${idPrefix}-s${i}`} className="text-xs font-medium text-ink-soft">
              С
            </label>
            <select
              id={`${idPrefix}-s${i}`}
              disabled={disabled}
              value={w.startMinute}
              onChange={(e) => set(i, { startMinute: Number(e.target.value) })}
              className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink"
            >
              {START_OPTIONS.map((m) => (
                <option key={m} value={m}>
                  {optionLabel(m)}
                </option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1">
            <label htmlFor={`${idPrefix}-e${i}`} className="text-xs font-medium text-ink-soft">
              До
            </label>
            <select
              id={`${idPrefix}-e${i}`}
              disabled={disabled}
              value={w.endMinute}
              onChange={(e) => set(i, { endMinute: Number(e.target.value) })}
              className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink"
            >
              {END_OPTIONS.map((m) => (
                <option key={m} value={m}>
                  {optionLabel(m)}
                </option>
              ))}
            </select>
          </div>
          <button
            type="button"
            disabled={disabled}
            onClick={() => onChange(windows.filter((_, j) => j !== i))}
            aria-label={`Убрать окно ${i + 1}`}
            className="flex h-11 w-11 items-center justify-center rounded-full border border-line bg-white text-ink-soft hover:border-line-strong disabled:opacity-40"
          >
            <Icon name="x" size={16} strokeWidth={1.8} />
          </button>
        </div>
      ))}
      {windows.length < 3 && (
        <Button type="button" variant="ghost" size="sm" className="min-h-[44px] self-start" disabled={disabled} onClick={() => onChange([...windows, { startMinute: 1080, endMinute: 1320 }])}>
          <Icon name="plus" size={14} strokeWidth={1.8} /> Добавить окно
        </Button>
      )}
      {error && (
        <p role="alert" className="text-xs text-danger" data-testid="windows-error">
          {error}
        </p>
      )}
    </div>
  )
}
