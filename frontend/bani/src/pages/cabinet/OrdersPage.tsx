import { SessionsList } from '@/components/slots/services/staff/SessionsList'
import { useSlotVertical } from '@/components/slots/SlotVerticalContext'
import { can } from '@/utils/slots/slotPermissions'
import { useBathsCompany } from '../../cabinet/cabinetVertical'

/** `/cabinet/:companyId/orders` (`ViewBookings`) — the bookings: «Ожидают проверки оплаты» first, then waiting for payment and confirmed. */
export function OrdersPage() {
  const { company } = useBathsCompany()
  const { NotFound } = useSlotVertical()
  if (!can(company.myPermissions, 'ViewBookings')) return <NotFound title="Раздел недоступен" />
  return (
    <main className="mx-auto max-w-[900px] px-4 pb-10 pt-8 sm:px-8">
      <h2 className="mb-1 font-serif text-[26px] text-ink">Брони</h2>
      <p className="mb-5 text-sm text-ink-soft">Сначала те, где гость приложил подтверждение оплаты: проверьте их и подтвердите или отклоните.</p>
      <SessionsList companyId={company.id} awaitingCount={company.awaitingPaymentCount ?? undefined} />
    </main>
  )
}
