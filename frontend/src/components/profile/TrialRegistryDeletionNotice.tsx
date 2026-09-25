import { Icon } from '../ui/Icon'

/**
 * O8 (API_CONTRACT_CYCLE18.md §377). Unlike `GuestDataGateNotice`, this text is NOT a static legal
 * key fetched separately — the server composes it per-caller (`trialRegistryNotice` is non-null
 * only for an owner who was ever granted a trial) and hands it over already-final, so the component
 * just prints it verbatim (§371 п.1) and renders nothing when it's `null`.
 *
 * Placed next to `GuestDataGateNotice` on `/profile/delete-account`, not instead of it (§371 п.12):
 * the two notices answer different questions and neither one supersedes the other.
 */
export function TrialRegistryDeletionNotice({ text }: { text: string | null }) {
  if (!text) return null

  return (
    <div className="bg-info-bg text-info text-sm px-4 py-3 rounded-xl flex items-start gap-2">
      <Icon name="alert-circle" size={15} strokeWidth={1.8} className="shrink-0 mt-0.5" />
      <p>{text}</p>
    </div>
  )
}
