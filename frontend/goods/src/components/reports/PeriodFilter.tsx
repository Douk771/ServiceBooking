import { useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { RadioChips } from '../pickup/RadioChips'
import { PERIOD_PRESETS } from '../../utils/reports'
import type { ReportPeriodPreset } from '../../types'

interface Props {
  period: ReportPeriodPreset
  from: string
  to: string
  onChange: (next: { period: ReportPeriodPreset; from: string; to: string }) => void
}

/**
 * Period presets are chosen at once; a custom range is applied with its own button, so a half-typed date never fires a
 * request. Which dates a preset means is the server's (`ReportPeriodDto`), not ours (API_CONTRACT_CYCLE25.md §522).
 */
export function PeriodFilter({ period, from, to, onChange }: Props) {
  const [draftFrom, setDraftFrom] = useState(from)
  const [draftTo, setDraftTo] = useState(to)
  const apply = () => onChange({ period: 'Custom', from: draftFrom, to: draftTo })
  return (
    <div className="flex flex-col gap-3">
      <RadioChips<ReportPeriodPreset>
        label="Период"
        value={period}
        onChange={(p) => onChange({ period: p, from: p === 'Custom' ? draftFrom : '', to: p === 'Custom' ? draftTo : '' })}
        options={PERIOD_PRESETS.map((p) => ({ value: p.value, label: p.label }))}
        className="flex flex-wrap gap-2"
      />
      {period === 'Custom' && (
        <div className="flex items-end gap-3 flex-wrap">
          <Input label="С" type="date" value={draftFrom} onChange={(e) => setDraftFrom(e.target.value)} />
          <Input label="По" type="date" value={draftTo} onChange={(e) => setDraftTo(e.target.value)} />
          <Button type="button" variant="secondary" disabled={!draftFrom || !draftTo} onClick={apply}>
            Применить
          </Button>
        </div>
      )}
    </div>
  )
}
