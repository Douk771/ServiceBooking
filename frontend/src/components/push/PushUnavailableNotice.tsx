import { Icon } from '../ui/Icon'
import { PUSH_UNAVAILABLE_MESSAGES, type PushUnavailableReason } from '../../utils/pushAvailability'

/**
 * ARCHITECTURE_CYCLE9.md §105.10 (US-118) — one explanation per reason, never a generic "notifications
 * unavailable". `reason` is picked by useWebPush()/getPushUnavailableReason and just printed here
 * verbatim: this component makes no decisions of its own.
 */
export function PushUnavailableNotice({ reason }: { reason: PushUnavailableReason }) {
  return (
    <div className="rounded-xl bg-cream-deep text-ink-soft text-sm px-4 py-3 flex items-start gap-2.5">
      <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5 text-muted" />
      <span>{PUSH_UNAVAILABLE_MESSAGES[reason]}</span>
    </div>
  )
}
