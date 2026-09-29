import { useQueryClient } from '@tanstack/react-query'
import { useShopContext } from '../../hooks/useShop'
import { SetupChecklist } from '../../components/hours/SetupChecklist'
import { WorkingHoursEditor } from '../../components/hours/WorkingHoursEditor'
import { PickupSettingsForm } from '../../components/hours/PickupSettingsForm'
import { SpecialDays } from '../../components/hours/SpecialDays'

/**
 * `/cabinet/:shopId/hours` (owner) — weekly hours, special days (P1), pick-up time settings and the setup checklist.
 * Every save refreshes the shop (`['shop', id]`, so the checklist and the acceptance verdict follow) and the ordering status.
 */
export function HoursPage() {
  const { shop } = useShopContext()
  const qc = useQueryClient()
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: ['shop', shop.id] })
    void qc.invalidateQueries({ queryKey: ['ordering-status', shop.id] })
  }
  return (
    <main className="max-w-[860px] mx-auto px-4 sm:px-8 pt-8 pb-12 flex flex-col gap-6">
      <SetupChecklist items={shop.setupChecklist} />
      {!shop.acceptingOrders && shop.notAcceptingReason && (
        <p role="status" className="rounded-xl bg-warning-bg text-warning text-sm font-medium px-4 py-3">
          {shop.notAcceptingReason}
        </p>
      )}
      <WorkingHoursEditor shopId={shop.id} onSaved={refresh} />
      <PickupSettingsForm key={JSON.stringify(shop.pickupSettings)} shopId={shop.id} initial={shop.pickupSettings} onSaved={refresh} />
      <SpecialDays shopId={shop.id} onChanged={refresh} />
    </main>
  )
}
