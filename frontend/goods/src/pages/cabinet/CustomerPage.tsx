import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { Pagination } from '@/components/ui/Pagination'
import { useLegalText } from '@/hooks/useLegalText'
import { formatRub } from '@/utils/money'
import { customersApi } from '../../api/customers'
import { useShopContext } from '../../hooks/useShop'
import { ErrorState, InlineError, LoadingList } from '../../components/StatePanels'
import { OrderHistoryTable } from '../../components/reports/OrderHistoryTable'
import { OrderDetailsModal } from '../../components/orders/OrderDetailsModal'
import { NotFoundPage } from '../NotFoundPage'
import { telHref } from '@/utils/phone'
import { getGoodsErrorMessage, httpStatus } from '../../utils/orderError'
import { NOTE_MAX_LENGTH, noteLength } from '../../utils/reports'
import type { OrderHistoryRowDto, ShopCustomerCardDto, ShopCustomerNoteDto } from '../../types'

const NOTE_FALLBACK = 'Заметку видят только сотрудники этого магазина. Покупатель её не видит.'

/**
 * `/cabinet/:shopId/customers/:customerRef` (US-25-09/10) — one buyer within THIS shop. `customerRef` is the id of any of
 * their orders; the phone never appears in the address. A foreign or erased `customerRef` is a 404 (empty body).
 */
export function CustomerPage() {
  const { shop } = useShopContext()
  const { customerRef = '' } = useParams<{ customerRef: string }>()
  const navigate = useNavigate()
  const [page, setPage] = useState(1)
  const [opened, setOpened] = useState<OrderHistoryRowDto | null>(null)
  const q = useQuery({
    queryKey: ['shop-customer', shop.id, customerRef, page],
    queryFn: () => customersApi.card(shop.id, customerRef, page),
    placeholderData: (prev) => prev,
    retry: (count, err) => httpStatus(err) === undefined && count < 1,
  })

  if (q.isLoading)
    return (
      <main className="max-w-[900px] mx-auto px-4 sm:px-8 pt-8">
        <LoadingList rows={3} rowClass="h-24" />
      </main>
    )
  if (q.isError && !q.data) {
    const status = httpStatus(q.error)
    if (status === 404) return <NotFoundPage title="Покупатель не найден" hint="Заказ мог быть обезличен по просьбе покупателя, или ссылка неверна." />
    return (
      <main className="max-w-[900px] mx-auto px-4 sm:px-8 pt-8">
        <ErrorState message={getGoodsErrorMessage(q.error, 'Не удалось загрузить карточку покупателя.')} onRetry={() => void q.refetch()} />
      </main>
    )
  }
  const card = q.data
  if (!card) return null

  return (
    <main className="max-w-[900px] mx-auto px-4 sm:px-8 pt-8 pb-14">
      <button type="button" onClick={() => navigate(-1)} className="inline-flex items-center gap-1.5 text-sm text-ink-soft hover:text-ink min-h-[44px]">
        <Icon name="chevron-left" size={15} strokeWidth={1.8} />
        Назад
      </button>

      <header className="mt-2 mb-6">
        <h2 className="font-serif text-[30px] text-ink" data-testid="customer-name">{card.name || 'Покупатель'}</h2>
        <p className="mt-1 flex items-center gap-2 flex-wrap text-sm">
          <a href={telHref(card.phone) || undefined} className="inline-flex items-center gap-1.5 font-medium text-ink underline underline-offset-2 hover:no-underline min-h-[36px]">
            <Icon name="phone" size={14} strokeWidth={1.8} />
            {card.phoneDisplay}
          </a>
          {card.phoneVerified && <span className="text-xs text-muted">номер подтверждён</span>}
        </p>
        <p className="mt-2 text-sm text-ink-soft" data-testid="customer-stats">{card.statsText}</p>
      </header>

      <dl className="grid grid-cols-2 sm:grid-cols-4 gap-x-6 gap-y-4 rounded-2xl border border-line bg-white p-5 mb-8">
        <Stat label="Заказов" value={String(card.ordersTotal)} />
        <Stat label="Выдано" value={`${card.issuedCount} на ${formatRub(card.issuedAmount)}`} />
        <Stat label="Отменил сам" value={String(card.cancelledByCustomer)} />
        <Stat label="Не забрал" value={String(card.notPickedUp)} />
      </dl>

      <NoteSection key={customerRef} shopId={shop.id} customerRef={customerRef} note={card.note ?? null} cardQueryKey={['shop-customer', shop.id, customerRef, page]} card={card} />

      <section aria-labelledby="cust-orders" className="mt-10">
        <h3 id="cust-orders" className="font-serif text-xl text-ink mb-3">Заказы в этом магазине</h3>
        {card.orders.items.length === 0 ? (
          <p className="text-sm text-muted">Заказов нет.</p>
        ) : (
          <>
            <OrderHistoryTable shopId={shop.id} rows={card.orders.items} caption="Заказы покупателя в этом магазине" showCustomer={false} onOpen={setOpened} />
            <Pagination
              page={card.orders.page}
              pageSize={card.orders.pageSize}
              total={card.orders.totalCount}
              hasNext={card.orders.page * card.orders.pageSize < card.orders.totalCount}
              onPageChange={setPage}
            />
          </>
        )}
      </section>

      <p className="text-xs text-muted mt-8">
        <Link to={`/cabinet/${shop.id}/history`} className="underline">К истории заказов</Link>
      </p>

      {opened && <OrderDetailsModal shopId={shop.id} orderId={opened.orderId} number={opened.number} onClose={() => setOpened(null)} />}
    </main>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs uppercase tracking-wider text-muted">{label}</dt>
      <dd className="font-serif text-xl text-ink">{value}</dd>
    </div>
  )
}

function NoteSection({ shopId, customerRef, note, cardQueryKey, card }: { shopId: string; customerRef: string; note: ShopCustomerNoteDto | null; cardQueryKey: unknown[]; card: ShopCustomerCardDto }) {
  const qc = useQueryClient()
  const notice = useLegalText('ShopCustomerNoteNotice')
  const [text, setText] = useState(note?.text ?? '')
  const [saved, setSaved] = useState(false)
  useEffect(() => {
    setText(note?.text ?? '')
  }, [note?.text])

  const save = useMutation({
    mutationFn: (value: string) => customersApi.putNote(shopId, customerRef, value.trim() ? value : null),
    onSuccess: (state) => {
      qc.setQueryData<ShopCustomerCardDto>(cardQueryKey, { ...card, note: state.note })
      void qc.invalidateQueries({ queryKey: ['shop-customer-note', shopId] })
      setText(state.note?.text ?? '')
      setSaved(true)
    },
  })

  const len = noteLength(text)
  const tooLong = len > NOTE_MAX_LENGTH
  const dirty = text.trim() !== (note?.text ?? '').trim()
  const html = notice.data?.contentHtml

  return (
    <section aria-labelledby="cust-note" className="rounded-2xl border border-line bg-white p-5">
      <h3 id="cust-note" className="font-serif text-xl text-ink">Заметка о покупателе</h3>
      <label htmlFor="cust-note-text" className="sr-only">Текст заметки</label>
      <textarea
        id="cust-note-text"
        value={text}
        rows={4}
        aria-describedby="cust-note-hint cust-note-count"
        onChange={(e) => {
          setText(e.target.value)
          setSaved(false)
          save.reset()
        }}
        className="mt-3 w-full rounded-xl border border-line px-4 py-3 text-sm bg-white text-ink outline-none focus:border-gold focus:ring-[3px] focus:ring-cream-deep"
      />
      <div className="flex items-start justify-between gap-3 mt-1 flex-wrap">
        <div id="cust-note-hint" className="text-xs text-muted max-w-[560px]" data-testid="note-notice">
          {html ? <div dangerouslySetInnerHTML={{ __html: html }} className="[&_p]:mb-1 [&_a]:underline" /> : notice.isLoading ? null : NOTE_FALLBACK}
        </div>
        <p id="cust-note-count" className={`text-xs ${tooLong ? 'text-danger font-semibold' : 'text-muted'}`} data-testid="note-count">
          {len} / {NOTE_MAX_LENGTH}
        </p>
      </div>
      {note && <p className="text-xs text-muted mt-2" data-testid="note-author">{note.updatedText}</p>}
      {save.isError && (
        <div className="mt-3"><InlineError>{getGoodsErrorMessage(save.error, 'Не удалось сохранить заметку.')}</InlineError></div>
      )}
      <div className="mt-3 flex items-center gap-3 flex-wrap">
        <Button loading={save.isPending} disabled={!dirty || tooLong} onClick={() => save.mutate(text)}>
          {text.trim() ? 'Сохранить заметку' : 'Удалить заметку'}
        </Button>
        {saved && !dirty && <span role="status" className="text-sm text-success font-medium">Сохранено</span>}
      </div>
    </section>
  )
}
