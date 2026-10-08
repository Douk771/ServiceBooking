import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { InlineError } from '@/components/ui/InlineError'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { staysBoardApi } from '../../api/staysBoard'
import type { BoardHouseDto, HouseBlockKind, StaysConflictDto } from '../../types'
import { BLOCK_COMMENT_MAX, BLOCK_KINDS, blockHouses, toBlockInput, toLastNight, validateBlock } from '../../utils/blockForm'
import { formatDateShort, formatDateNumeric, nightsLabel } from '../../utils/stayDates'
import { getStayErrorMessage, readConflict } from '../../utils/stayError'
import { StayNotice } from '../StayNotice'

export interface BlockDialogTarget {
  /** An existing block (edit/delete), or null for a new one. */
  block: { id: string; houseId: string; startDate: string; endDate: string; kind: HouseBlockKind; comment?: string | null } | null
  houseId?: string
  firstNight?: string
}

function explain(err: unknown): string {
  const c = readConflict<StaysConflictDto>(err)
  if (c?.code === 'BlockConflictsWithBooking' && c.conflicts?.length) {
    const first = c.conflicts[0]
    return c.message || `Даты заняты бронью ${first.guestName ?? ''}, ${formatDateNumeric(first.checkInDate)}–${formatDateNumeric(first.checkOutDate)}`
  }
  return c?.message ?? getStayErrorMessage(err, 'Не удалось сохранить блокировку.')
}

/**
 * Block dates of houses (`ManageBlocks`, US-37-15/16): repair, personal use, other. Nights are shown «с … по …» (the last night); a block
 * can cover several houses at once (one request per house). A booking in the way is named with a link to it — a block never steals dates.
 * The comment is the owner's note — «Не вносите паспортные данные».
 */
export function BlockDialog({
  companyId,
  houses,
  target,
  today,
  onClose,
  onChanged,
}: {
  companyId: string
  houses: BoardHouseDto[]
  target: BlockDialogTarget
  today: string
  onClose: () => void
  /** The board changed (a block saved or removed): refetch it. The dialog stays open unless the whole job is done. */
  onChanged: () => void
}) {
  const editing = target.block !== null
  const [houseIds, setHouseIds] = useState<string[]>(target.block ? [target.block.houseId] : target.houseId ? [target.houseId] : [])
  const [firstNight, setFirstNight] = useState(target.block?.startDate ?? target.firstNight ?? '')
  const [lastNight, setLastNight] = useState(target.block ? toLastNight(target.block.endDate) : (target.firstNight ?? ''))
  const [kind, setKind] = useState<HouseBlockKind>(target.block?.kind ?? 'Repair')
  // The board item carries the block's comment (`BoardItemDto.comment`): an edit keeps it instead of silently erasing it.
  const [comment, setComment] = useState(target.block?.comment ?? '')
  const [errors, setErrors] = useState<{ dates?: string; comment?: string; houses?: string }>({})
  const [formError, setFormError] = useState('')
  const [conflicts, setConflicts] = useState<NonNullable<StaysConflictDto['conflicts']>>([])
  const [pending, setPending] = useState(false)
  const [results, setResults] = useState<{ name: string; ok: boolean; error?: string }[]>([])
  const [confirmDelete, setConfirmDelete] = useState(false)

  const nameOf = (id: string) => houses.find((h) => h.id === id)?.name ?? id
  const choices = houses.filter((h) => !h.isArchived)

  const toggleHouse = (id: string) => setHouseIds((cur) => (cur.includes(id) ? cur.filter((x) => x !== id) : [...cur, id]))

  async function submit() {
    setFormError('')
    setConflicts([])
    setResults([])
    const v = validateBlock({ houseIds, firstNight, lastNight, comment }, today, { editing })
    setErrors(v)
    if (Object.keys(v).length > 0) return
    setPending(true)
    try {
      if (target.block) {
        await staysBoardApi.updateBlock(companyId, target.block.id, toBlockInput(houseIds[0], { firstNight, lastNight, kind, comment }))
        onChanged()
        onClose()
        return
      }
      const res = await blockHouses(houseIds, (id) => staysBoardApi.createBlock(companyId, toBlockInput(id, { firstNight, lastNight, kind, comment })), explain)
      const summary = res.map((r) => ({ name: nameOf(r.houseId), ok: r.ok, error: r.error }))
      if (summary.every((r) => r.ok)) {
        onChanged()
        onClose()
        return
      }
      // Some houses are blocked, some are not: say which, and let the owner fix the rest (the blocked ones are dropped from the form).
      setResults(summary)
      setHouseIds(res.filter((r) => !r.ok).map((r) => r.houseId))
      if (summary.some((r) => r.ok)) onChanged()
    } catch (err) {
      const c = readConflict<StaysConflictDto>(err)
      setConflicts(c?.conflicts ?? [])
      setFormError(explain(err))
    } finally {
      setPending(false)
    }
  }

  async function remove() {
    if (!target.block) return
    setPending(true)
    setFormError('')
    try {
      await staysBoardApi.deleteBlock(companyId, target.block.id)
      onChanged()
      onClose()
    } catch (err) {
      setFormError(getStayErrorMessage(err, 'Не удалось снять блокировку.'))
      setConfirmDelete(false)
    } finally {
      setPending(false)
    }
  }

  return (
    <Modal title={editing ? 'Блокировка дат' : 'Заблокировать даты'} onClose={onClose} dismissible={!pending}>
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault()
          void submit()
        }}
      >
        {!editing && (
          <fieldset className="flex flex-col gap-1">
            <legend className="mb-1 text-[13px] font-medium text-[#4A4038]">Дома</legend>
            {choices.map((h) => (
              <label key={h.id} className="flex min-h-[44px] cursor-pointer items-center gap-3 text-sm text-ink">
                <input type="checkbox" className="h-5 w-5 accent-gold" checked={houseIds.includes(h.id)} onChange={() => toggleHouse(h.id)} />
                {h.name}
              </label>
            ))}
            {errors.houses && <p className="text-xs text-danger">{errors.houses}</p>}
          </fieldset>
        )}
        {editing && <p className="text-sm text-ink">Дом: <span className="font-semibold">{nameOf(houseIds[0])}</span></p>}

        <div className="grid grid-cols-2 gap-3">
          <Input label="Первая ночь" type="date" value={firstNight} onChange={(e) => { setFirstNight(e.target.value); if (!lastNight || lastNight < e.target.value) setLastNight(e.target.value) }} />
          <Input label="Последняя ночь" type="date" value={lastNight} min={firstNight || undefined} onChange={(e) => setLastNight(e.target.value)} />
        </div>
        {firstNight && lastNight && lastNight >= firstNight && (
          <p className="-mt-2 text-xs text-muted">
            {nightsLabel(Math.round((Date.parse(lastNight) - Date.parse(firstNight)) / 86_400_000) + 1)}: {formatDateShort(firstNight)} — {formatDateShort(lastNight)}; свободно с следующего дня.
          </p>
        )}
        {errors.dates && <p role="alert" className="-mt-2 text-xs text-danger">{errors.dates}</p>}

        <div className="flex flex-col gap-1.5">
          <label htmlFor="block-kind" className="text-[13px] font-medium text-[#4A4038]">
            Причина
          </label>
          <select id="block-kind" value={kind} onChange={(e) => setKind(e.target.value as HouseBlockKind)} className="min-h-[44px] rounded-xl border border-line bg-white px-3 text-sm text-ink">
            {BLOCK_KINDS.map((k) => (
              <option key={k.value} value={k.value}>
                {k.label}
              </option>
            ))}
          </select>
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="block-comment" className="text-[13px] font-medium text-[#4A4038]">
            Комментарий <span className="font-normal text-muted">(для себя, до {BLOCK_COMMENT_MAX} символов)</span>
          </label>
          <textarea
            id="block-comment"
            rows={2}
            maxLength={BLOCK_COMMENT_MAX}
            value={comment}
            aria-describedby="block-notice"
            onChange={(e) => setComment(e.target.value)}
            className="rounded-xl border border-line bg-white px-3 py-2.5 text-sm text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
          />
          {errors.comment && <p className="text-xs text-danger">{errors.comment}</p>}
          <StayNotice textKey="StayMigrationOwnerNotice" id="block-notice" />
        </div>

        {formError && (
          <div>
            <InlineError>{formError}</InlineError>
            {conflicts.length > 0 && (
              <ul className="mt-2 text-sm text-ink-soft" data-testid="block-conflicts">
                {conflicts.map((c) => (
                  <li key={c.bookingId}>
                    <Link to={`/cabinet/${companyId}/bookings/${c.bookingId}`} className="font-medium text-gold-dark underline">
                      {c.guestName || 'Гость'}, {formatDateNumeric(c.checkInDate)}–{formatDateNumeric(c.checkOutDate)}
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
        {results.length > 0 && (
          <ul className="flex flex-col gap-1 text-sm" data-testid="block-results">
            {results.map((r) => (
              <li key={r.name} className={r.ok ? 'text-success' : 'text-danger'}>
                {r.name}: {r.ok ? 'заблокирован' : r.error}
              </li>
            ))}
          </ul>
        )}

        <div className="flex flex-wrap gap-3">
          {editing && !confirmDelete && (
            <Button type="button" variant="danger" className="min-h-[44px]" onClick={() => setConfirmDelete(true)} disabled={pending}>
              Снять блокировку
            </Button>
          )}
          {editing && confirmDelete && (
            <Button type="button" variant="danger" className="min-h-[44px]" loading={pending} onClick={() => void remove()}>
              Точно снять
            </Button>
          )}
          <div className="ml-auto flex gap-3">
            <Button type="button" variant="secondary" className="min-h-[44px]" onClick={onClose} disabled={pending}>
              Закрыть
            </Button>
            <Button type="submit" className="min-h-[44px]" loading={pending}>
              {editing ? 'Сохранить' : 'Заблокировать'}
            </Button>
          </div>
        </div>
      </form>
    </Modal>
  )
}
