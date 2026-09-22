import { useEffect, useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ru } from 'date-fns/locale'
import {
  adminBillingApi,
  type AdminBillingAccount,
  type AdminBillingAccountListItem,
  type AdminSubscribedOption,
  type AdminSubscriptionRequest,
  type SubscriptionStatus,
} from '../../api/adminBilling'
import { plansApi } from '../../api/plans'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { Icon } from '../../components/ui/Icon'
import { Pagination } from '../../components/ui/Pagination'
import { getAdminBillingErrorMessage as getBillingErrorMessage, isLimitOverflowConflict } from '../../utils/adminBillingError'
import {
  STATUS_BADGE_CLASS,
  formatRub,
  computeExpectedTotal,
  buildAssignInput,
  isPaidUntilMissing,
  type AssignOptionRow,
} from './billingAccountsHelpers'

function fmtDate(d: string | null | undefined) {
  return d ? format(parseISO(d), 'd MMM yyyy', { locale: ru }) : '—'
}

function fmtDateTime(d: string | null | undefined) {
  return d ? format(parseISO(d), 'd MMM yyyy, HH:mm', { locale: ru }) : '—'
}

// Cycle-3 envelope is {items, page, pageSize, totalCount}; <Pagination> was built for the older
// {total, hasNext} shape shared by the rest of the admin screens — adapted here rather than
// touching the shared component just for this one screen.
function toPagerProps(page: number, pageSize: number, totalCount: number) {
  return { page, pageSize, total: totalCount, hasNext: page * pageSize < totalCount }
}

// ── Assign subscription modal ─────────────────────────────────────────────────

interface AssignTarget {
  account: AdminBillingAccount
  /** Pending request being approved, if opened from the requests queue — its desired composition
   *  pre-fills the form and its id closes the request as Approved in the same call. */
  request?: AdminSubscriptionRequest
}

function AssignSubscriptionModal({ target, onClose }: { target: AssignTarget; onClose: () => void }) {
  const { account, request } = target
  const qc = useQueryClient()

  const { data: plans } = useQuery({ queryKey: ['admin-plans'], queryFn: plansApi.list })
  const { data: catalogOptions } = useQuery({ queryKey: ['admin-options'], queryFn: plansApi.listOptions })
  const activePlans = (plans ?? []).filter((p) => p.isActive)

  const [planId, setPlanId] = useState<string>(account.planId ?? '')
  const [isActive, setIsActive] = useState(account.isActive ?? true)
  const [paidUntil, setPaidUntil] = useState(account.paidUntil ? account.paidUntil.slice(0, 10) : '')
  const [amount, setAmount] = useState('')
  const [comment, setComment] = useState('')
  const [confirmOverflow, setConfirmOverflow] = useState(false)
  const [rows, setRows] = useState<AssignOptionRow[]>([])

  // Seed the option rows once from the account's current subscription (plus anything requested),
  // merged with the full catalog so options not yet subscribed can still be added here.
  useEffect(() => {
    if (!catalogOptions) return
    const currentByOption = new Map(account.options.map((o: AdminSubscribedOption) => [o.optionId, o]))
    const requestedByOption = new Map((request?.items ?? []).map((i) => [i.optionId, i.quantity]))
    setRows(
      catalogOptions
        .filter((o) => o.isActive && o.pricePerMonth != null)
        .map((o) => {
          const current = currentByOption.get(o.id)
          const requestedQty = requestedByOption.get(o.id)
          const selected = requestedQty != null ? true : !!current && current.status !== 'Ending'
          return {
            optionId: o.id,
            name: o.name,
            kind: o.kind,
            unitName: o.unitName,
            pricePerMonth: o.pricePerMonth ?? 0,
            selected,
            quantity: String(requestedQty ?? current?.quantity ?? 1),
          }
        }),
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [catalogOptions])

  const selectedPlan = activePlans.find((p) => p.id === planId)
  const planPrice = planId ? selectedPlan?.pricePerMonth ?? account.plan.pricePerMonth : 0
  const pricePerUnitByOption = Object.fromEntries(rows.map((r) => [r.optionId, r.pricePerMonth]))
  const expectedTotal = computeExpectedTotal(planPrice, rows, pricePerUnitByOption)

  // Free plan has no expiry — the field is hidden for it entirely, so a paid plan without a date is
  // the only real "missing" state. Last local guard before the server's own 400 (ARCHITECTURE_CYCLE6.md §43.3.6).
  const isFree = planId === ''
  const dateMissing = isPaidUntilMissing(planId, paidUntil)

  const mut = useMutation({
    mutationFn: () =>
      adminBillingApi.assignSubscription(
        account.id,
        buildAssignInput({
          planId: planId || null,
          isActive,
          paidUntil,
          rows,
          amount,
          comment,
          requestId: request?.id ?? null,
          confirmLimitOverflow: confirmOverflow,
        }),
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-billing-accounts'] })
      qc.invalidateQueries({ queryKey: ['admin-billing-account', account.id] })
      qc.invalidateQueries({ queryKey: ['admin-billing-account-history', account.id] })
      qc.invalidateQueries({ queryKey: ['admin-subscription-requests'] })
      onClose()
    },
  })

  const overflow = mut.isError && isLimitOverflowConflict(mut.error)

  const toggleRow = (optionId: string) =>
    setRows((rs) => rs.map((r) => (r.optionId === optionId ? { ...r, selected: !r.selected } : r)))
  const setQuantity = (optionId: string, value: string) =>
    setRows((rs) => rs.map((r) => (r.optionId === optionId ? { ...r, quantity: value } : r)))

  return (
    <Modal title={`Назначить подписку — ${account.ownerName}`} onClose={onClose}>
      <div className="flex flex-col gap-4">
        {request && (
          <p className="text-xs text-muted bg-info-bg rounded-xl px-3 py-2">
            Одобрение заявки от {fmtDateTime(request.createdAt)}: состав ниже предзаполнен желаемым.
          </p>
        )}
        <div>
          <label className="text-sm font-medium text-ink-soft block mb-1">Тарифный план</label>
          <select
            value={planId}
            onChange={(e) => setPlanId(e.target.value)}
            className="w-full rounded-xl border border-line px-3 py-2.5 text-sm outline-none focus:border-gold"
          >
            <option value="">Free (снять тариф)</option>
            {activePlans.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name} {p.pricePerMonth > 0 ? `— ${p.pricePerMonth.toLocaleString('ru-RU')} ₽/мес` : ''}
              </option>
            ))}
          </select>
          {selectedPlan && !selectedPlan.allowOnlineBooking && (
            <p className="text-xs text-warning mt-1.5 bg-warning-bg rounded-lg px-2.5 py-1.5">
              В этом тарифе онлайн-запись выключена — клиенты не смогут записаться сами.
            </p>
          )}
        </div>

        <div className={isFree ? 'grid grid-cols-1 gap-3' : 'grid grid-cols-2 gap-3'}>
          {!isFree && (
            <div className="flex flex-col gap-1.5">
              <label className="text-[13px] font-medium text-[#4A4038]">Оплачено до</label>
              <input
                type="date"
                required
                value={paidUntil}
                onChange={(e) => setPaidUntil(e.target.value)}
                className="rounded-xl border border-line px-3 py-2.5 text-sm outline-none focus:border-gold"
              />
              {dateMissing && <p className="text-xs text-danger">Укажите дату окончания подписки</p>}
            </div>
          )}
          <label className="flex items-center gap-2 self-end pb-2.5 cursor-pointer">
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} className="w-4 h-4 accent-gold" />
            <span className="text-sm text-ink-soft">Подписка активна</span>
          </label>
        </div>

        <div>
          <p className="text-sm font-medium text-ink-soft mb-2">Опции</p>
          <div className="rounded-xl border border-line px-3">
            {rows.length === 0 && <p className="text-xs text-muted py-3">Продаваемых опций в каталоге нет</p>}
            {rows.map((r) => (
              <div key={r.optionId} className="flex items-center justify-between gap-3 py-2 border-b border-line last:border-0">
                <label className="flex items-center gap-2.5 min-w-0 cursor-pointer">
                  <input type="checkbox" checked={r.selected} onChange={() => toggleRow(r.optionId)} className="w-4 h-4 accent-gold shrink-0" />
                  <span className="min-w-0">
                    <span className="text-sm text-ink truncate block">{r.name}</span>
                    <span className="text-xs text-muted">{formatRub(r.pricePerMonth)}{r.kind === 'Quantity' ? ` / ${r.unitName ?? 'ед.'}` : '/мес'}</span>
                  </span>
                </label>
                {r.kind === 'Quantity' && r.selected && (
                  <input
                    type="number"
                    min={1}
                    value={r.quantity}
                    onChange={(e) => setQuantity(r.optionId, e.target.value)}
                    className="w-16 rounded-lg border border-line px-2 py-1.5 text-xs outline-none focus:border-gold shrink-0"
                  />
                )}
              </div>
            ))}
          </div>
        </div>

        {/* Invariant made visible, not implied: итог = цена тарифа + Σ опция × количество. */}
        <div className="rounded-xl bg-cream-deep px-4 py-3 flex items-center justify-between">
          <span className="text-sm text-ink-soft">Итог в месяц (тариф + опции)</span>
          <span className="text-base font-semibold text-ink">{formatRub(expectedTotal)}</span>
        </div>

        <Input label="Сумма платежа (справочно)" type="number" min={0} value={amount} onChange={(e) => setAmount(e.target.value)} />
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-[#4A4038]">Комментарий</label>
          <textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            rows={2}
            className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold resize-none"
            placeholder="Счёт 114"
          />
        </div>

        {overflow && (
          <label className="flex items-start gap-2.5 bg-warning-bg rounded-xl px-3 py-2.5 cursor-pointer">
            <input
              type="checkbox"
              checked={confirmOverflow}
              onChange={(e) => setConfirmOverflow(e.target.checked)}
              className="w-4 h-4 accent-gold mt-0.5 shrink-0"
            />
            <span className="text-xs text-ink-soft">
              {getBillingErrorMessage(mut.error)} Подтвердите, что понимаете перерасход, и сохраните ещё раз.
            </span>
          </label>
        )}
        {mut.isError && !overflow && <p className="text-sm text-danger">{getBillingErrorMessage(mut.error)}</p>}

        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button className="flex-1" loading={mut.isPending} disabled={dateMissing} onClick={() => mut.mutate()}>
            Сохранить
          </Button>
        </div>
      </div>
    </Modal>
  )
}

// ── Reject request modal ──────────────────────────────────────────────────────

function RejectRequestModal({ request, onClose }: { request: AdminSubscriptionRequest; onClose: () => void }) {
  const qc = useQueryClient()
  const [comment, setComment] = useState('')
  const mut = useMutation({
    mutationFn: () => adminBillingApi.rejectRequest(request.id, comment || undefined),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-subscription-requests'] })
      onClose()
    },
  })

  return (
    <Modal title={`Отклонить заявку — ${request.requestedByName}`} onClose={onClose}>
      <div className="flex flex-col gap-4">
        <p className="text-xs text-muted bg-warning-bg rounded-xl px-3 py-2">
          Причина уходит владельцу — он увидит её в истории своих заявок.
        </p>
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-[#4A4038]">Причина отказа</label>
          <textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            rows={3}
            maxLength={500}
            className="rounded-xl border border-line px-3 py-2 text-sm outline-none focus:border-gold resize-none"
            placeholder="Например: тариф с такими опциями недоступен для вашего региона"
          />
        </div>
        {mut.isError && <p className="text-sm text-danger">{getBillingErrorMessage(mut.error, 'Не удалось отклонить заявку.')}</p>}
        <div className="flex gap-3 pt-1">
          <Button variant="secondary" className="flex-1" onClick={onClose}>
            Отмена
          </Button>
          <Button variant="danger" className="flex-1" loading={mut.isPending} onClick={() => mut.mutate()}>
            Отклонить
          </Button>
        </div>
      </div>
    </Modal>
  )
}

// ── Account detail ────────────────────────────────────────────────────────────

function AccountDetail({ accountId, onClose }: { accountId: string; onClose: () => void }) {
  const { data: account, isLoading } = useQuery({
    queryKey: ['admin-billing-account', accountId],
    queryFn: () => adminBillingApi.getAccount(accountId),
  })
  const { data: history } = useQuery({
    queryKey: ['admin-billing-account-history', accountId],
    queryFn: () => adminBillingApi.getHistory(accountId),
  })
  const [assigning, setAssigning] = useState(false)

  return (
    <Modal title={isLoading || !account ? 'Загрузка…' : `Аккаунт — ${account.ownerName}`} onClose={onClose}>
      {isLoading || !account ? (
        <div className="grid gap-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-16 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : (
        <div className="flex flex-col gap-5">
          {assigning && <AssignSubscriptionModal target={{ account }} onClose={() => setAssigning(false)} />}

          <div className="flex items-center justify-between flex-wrap gap-2">
            <div className="flex items-center gap-2 flex-wrap">
              <span
                className={`text-xs px-2 py-0.5 rounded-full font-medium ${STATUS_BADGE_CLASS[account.status]}`}
              >
                {account.statusText}
              </span>
              <span className="text-xs text-muted">{account.ownerPhoneMasked}</span>
              <span className="text-xs text-muted">оплачено до {fmtDate(account.paidUntil)}</span>
            </div>
            <Button size="sm" onClick={() => setAssigning(true)}>
              Назначить подписку
            </Button>
          </div>

          <div>
            <p className="text-sm font-medium text-ink-soft mb-1.5">Тариф — {account.plan.name}</p>
            <div className="rounded-xl border border-line divide-y divide-line">
              <div className="flex items-center justify-between px-3 py-2 text-sm">
                <span className="text-ink-soft">{account.plan.name}</span>
                <span className="text-ink">{formatRub(account.plan.pricePerMonth)}</span>
              </div>
              {account.options.map((o) => (
                <div key={o.optionId} className="flex items-center justify-between px-3 py-2 text-sm flex-wrap gap-1">
                  <span className="text-ink-soft">
                    {o.name}
                    {o.kind === 'Quantity' ? ` × ${o.quantity}` : ''}
                    <span className="text-xs text-muted ml-1.5">{o.statusText}</span>
                  </span>
                  <span className="text-ink">{formatRub(o.pricePerMonth)}</span>
                </div>
              ))}
              <div className="flex items-center justify-between px-3 py-2.5 bg-cream-deep font-semibold text-sm">
                <span>Итог в месяц</span>
                <span>{formatRub(account.totalMonthlyPrice)}</span>
              </div>
            </div>
            {account.grandfatheredEmployeeBonusText && (
              <p className="text-xs text-muted mt-1.5">{account.grandfatheredEmployeeBonusText}</p>
            )}
          </div>

          <div className="grid grid-cols-2 gap-3 text-sm">
            <div className="rounded-xl bg-cream-deep px-3 py-2.5">
              <p className="text-xs text-muted">Компании</p>
              <p className="text-ink font-medium">
                {account.companiesUsed} / {account.companiesLimit ?? '∞'}
              </p>
            </div>
            <div className="rounded-xl bg-cream-deep px-3 py-2.5">
              <p className="text-xs text-muted">Сотрудники</p>
              <p className="text-ink font-medium">
                {account.employeesUsed} / {account.employeesLimit ?? '∞'}
              </p>
            </div>
            <div className="rounded-xl bg-cream-deep px-3 py-2.5">
              <p className="text-xs text-muted">Номера</p>
              <p className="text-ink font-medium">
                оплачено {account.numbersPaid} из {account.numbersRegistered}
              </p>
            </div>
          </div>

          <div>
            <p className="text-sm font-medium text-ink-soft mb-1.5">Компании аккаунта</p>
            <div className="flex flex-col gap-1.5">
              {account.companies.map((c) => (
                <div key={c.companyId} className="flex items-center justify-between text-sm px-3 py-2 rounded-xl bg-cream-deep">
                  <span className="text-ink">{c.companyName}</span>
                  <span className="text-xs text-muted">{c.ownerName ?? '—'} · {c.employeeCount} сотр.</span>
                </div>
              ))}
              {account.companies.length === 0 && <p className="text-xs text-muted">Компаний нет</p>}
            </div>
          </div>

          <div>
            <p className="text-sm font-medium text-ink-soft mb-1.5">Номера рассылки</p>
            <div className="flex flex-col gap-1.5">
              {account.channels.map((c) => (
                <div key={c.channelId} className="flex items-center justify-between text-sm px-3 py-2 rounded-xl bg-cream-deep flex-wrap gap-1">
                  <span className="text-ink">{c.phoneMasked ?? '—'}</span>
                  <span className="text-xs text-muted">
                    {c.state} ·{' '}
                    {c.fundingState === 'Funded' ? 'оплачен' : c.fundingState === 'Unfunded' ? 'сверх оплаченного' : 'не оплачен'} ·{' '}
                    {c.assignedCompanies} комп. · с {fmtDate(c.createdAt)}
                  </span>
                </div>
              ))}
              {account.channels.length === 0 && <p className="text-xs text-muted">Номеров нет</p>}
            </div>
          </div>

          <div>
            <p className="text-sm font-medium text-ink-soft mb-1.5">История изменений подписки</p>
            <div className="flex flex-col gap-2 max-h-64 overflow-y-auto">
              {(history ?? []).map((h) => (
                <div key={h.id} className="text-xs bg-cream-deep rounded-xl p-2.5">
                  <div className="flex items-center justify-between text-muted flex-wrap gap-1">
                    <span>{fmtDateTime(h.changedAt)}</span>
                    <span>{h.changedByName}</span>
                    {h.changeKind === 'Legacy' && (
                      <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-line text-ink-soft">унаследовано</span>
                    )}
                  </div>
                  <div className="text-ink-soft mt-1">
                    {h.oldPlanName && h.newPlanName && h.oldPlanName !== h.newPlanName && (
                      <span>
                        {h.oldPlanName} → <span className="font-medium">{h.newPlanName}</span>{' '}
                      </span>
                    )}
                    {h.newOptionsSummary && <span>{h.newOptionsSummary} </span>}
                    {h.newPaidUntil && <span>· до {fmtDate(h.newPaidUntil)}</span>}
                    {h.amount != null && <span> · {formatRub(h.amount)}</span>}
                  </div>
                  {h.comment && <div className="text-muted mt-0.5 italic">{h.comment}</div>}
                </div>
              ))}
              {(history ?? []).length === 0 && <p className="text-xs text-muted">Записей пока нет</p>}
            </div>
          </div>
        </div>
      )}
    </Modal>
  )
}

// ── Accounts list ──────────────────────────────────────────────────────────────

const STATUS_OPTIONS: { value: SubscriptionStatus | ''; label: string }[] = [
  { value: '', label: 'Все статусы' },
  { value: 'Free', label: 'Бесплатный' },
  { value: 'Active', label: 'Активна' },
  { value: 'Expired', label: 'Истекла' },
]

function AccountRow({ item, onOpen }: { item: AdminBillingAccountListItem; onOpen: () => void }) {
  return (
    <Card className="p-4 flex items-center justify-between gap-4 flex-wrap cursor-pointer hover:border-line-strong" onClick={onOpen}>
      <div className="min-w-0">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="font-medium text-ink">{item.ownerName}</span>
          {item.name && <span className="text-xs text-muted">({item.name})</span>}
          <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${STATUS_BADGE_CLASS[item.status]}`}>
            {item.statusText}
          </span>
          {item.hasPendingRequest && (
            <span className="text-xs px-2 py-0.5 rounded-full bg-info-bg text-info">заявка</span>
          )}
        </div>
        <p className="text-xs text-muted mt-0.5">
          {item.planName ?? 'Free'} · {formatRub(item.totalMonthlyPrice ?? 0)}/мес · оплачено до {fmtDate(item.paidUntil)}
        </p>
        <p className="text-xs text-muted mt-0.5">
          {item.companiesUsed}/{item.companiesLimit ?? '∞'} компаний · {item.employeesUsed}/{item.employeesLimit ?? '∞'} сотр. ·{' '}
          номеров {item.numbersPaid}/{item.numbersRegistered}
        </p>
      </div>
      <Icon name="chevron-right" size={16} strokeWidth={1.8} className="text-muted shrink-0" />
    </Card>
  )
}

function AccountsListSection() {
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<SubscriptionStatus | ''>('')
  const [page, setPage] = useState(1)
  const [openAccountId, setOpenAccountId] = useState<string | null>(null)

  const { data, isLoading } = useQuery({
    queryKey: ['admin-billing-accounts', search, status, page],
    queryFn: () => adminBillingApi.listAccounts({ search: search || undefined, status: status || undefined, page, pageSize: 20 }),
  })

  const handleSearch = (v: string) => {
    setSearch(v)
    setPage(1)
  }

  return (
    <div>
      {openAccountId && <AccountDetail accountId={openAccountId} onClose={() => setOpenAccountId(null)} />}
      <div className="flex flex-wrap gap-3 mb-4">
        <div className="flex-1 min-w-[200px]">
          <Input placeholder="Поиск по держателю, телефону, email, компании..." value={search} onChange={(e) => handleSearch(e.target.value)} />
        </div>
        <select
          value={status}
          onChange={(e) => {
            setStatus(e.target.value as SubscriptionStatus | '')
            setPage(1)
          }}
          className="rounded-xl border border-line px-3 py-2.5 text-sm outline-none focus:border-gold bg-white text-ink"
        >
          {STATUS_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      </div>

      {isLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="h-20 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : (
        <div className="grid gap-3">
          {(data?.items ?? []).map((a) => (
            <AccountRow key={a.id} item={a} onOpen={() => setOpenAccountId(a.id)} />
          ))}
          {data?.items.length === 0 && (
            <Card className="p-10 text-center text-muted">
              <Icon name="credit-card" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
              <p>Биллинг-аккаунтов не найдено</p>
            </Card>
          )}
        </div>
      )}
      {data && <Pagination {...toPagerProps(data.page, data.pageSize, data.totalCount)} onPageChange={setPage} />}
    </div>
  )
}

// ── Requests queue ─────────────────────────────────────────────────────────────

function RequestsQueueSection() {
  const [page, setPage] = useState(1)
  const [approving, setApproving] = useState<{ requestId: string; account: AdminBillingAccount } | null>(null)
  const [rejecting, setRejecting] = useState<AdminSubscriptionRequest | null>(null)
  const [loadErrorFor, setLoadErrorFor] = useState<string | null>(null)

  const { data, isLoading } = useQuery({
    queryKey: ['admin-subscription-requests', page],
    queryFn: () => adminBillingApi.listRequests({ status: 'Pending', page, pageSize: 20 }),
  })

  const openApprove = async (req: AdminSubscriptionRequest) => {
    setLoadErrorFor(null)
    try {
      const account = await adminBillingApi.getAccount(req.billingAccountId)
      setApproving({ requestId: req.id, account })
    } catch {
      setLoadErrorFor(req.id)
    }
  }

  return (
    <div>
      {approving && (
        <AssignSubscriptionModal
          target={{
            account: approving.account,
            request: data?.items.find((r) => r.id === approving.requestId),
          }}
          onClose={() => setApproving(null)}
        />
      )}
      {rejecting && <RejectRequestModal request={rejecting} onClose={() => setRejecting(null)} />}

      {isLoading ? (
        <div className="grid gap-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-24 bg-cream-deep rounded-2xl animate-pulse" />
          ))}
        </div>
      ) : (
        <div className="grid gap-3">
          {(data?.items ?? []).map((r) => (
            <Card key={r.id} className="p-4 flex items-center justify-between gap-4 flex-wrap">
              <div className="min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="font-medium text-ink">{r.requestedByName}</span>
                  {r.requestedByPhoneMasked && <span className="text-xs text-muted">{r.requestedByPhoneMasked}</span>}
                  <span className="text-xs text-muted">{fmtDateTime(r.createdAt)}</span>
                </div>
                <p className="text-xs text-muted mt-0.5">
                  {r.currentPlanName ?? 'Free'}
                  {r.desiredPlanName && r.desiredPlanName !== r.currentPlanName ? ` → ${r.desiredPlanName}` : ''} ·{' '}
                  {r.items.map((i) => `${i.name} × ${i.quantity}`).join(', ') || 'без опций'}
                </p>
                <p className="text-xs text-muted mt-0.5">Итог: {formatRub(r.estimatedMonthlyPrice)}/мес · {r.companiesCount ?? 0} компаний</p>
                {r.comment && <p className="text-xs text-muted italic mt-0.5">«{r.comment}»</p>}
                {loadErrorFor === r.id && <p className="text-xs text-danger mt-1">Не удалось загрузить аккаунт заявки.</p>}
              </div>
              <div className="flex gap-2 shrink-0">
                <Button size="sm" onClick={() => openApprove(r)}>
                  Одобрить
                </Button>
                <Button size="sm" variant="danger" onClick={() => setRejecting(r)}>
                  Отклонить
                </Button>
              </div>
            </Card>
          ))}
          {data?.items.length === 0 && (
            <Card className="p-10 text-center text-muted">
              <Icon name="check-circle" size={28} strokeWidth={1.6} className="mx-auto mb-2" />
              <p>Заявок в очереди нет</p>
            </Card>
          )}
        </div>
      )}
      {data && <Pagination {...toPagerProps(data.page, data.pageSize, data.totalCount)} onPageChange={setPage} />}
    </div>
  )
}

// ── Main tab ────────────────────────────────────────────────────────────────

type SubTab = 'accounts' | 'requests'

export function BillingAccountsAdminTab() {
  const [sub, setSub] = useState<SubTab>('accounts')

  return (
    <div>
      <div className="flex gap-1 bg-cream-deep p-1 rounded-full mb-5 w-fit">
        {([
          { key: 'accounts' as const, label: 'Аккаунты' },
          { key: 'requests' as const, label: 'Заявки' },
        ]).map((t) => (
          <button
            key={t.key}
            onClick={() => setSub(t.key)}
            className={`px-4 py-[7px] rounded-full text-xs font-semibold transition-all ${sub === t.key ? 'bg-white text-ink' : 'text-gold-dark hover:text-ink'}`}
          >
            {t.label}
          </button>
        ))}
      </div>
      {sub === 'accounts' ? <AccountsListSection /> : <RequestsQueueSection />}
    </div>
  )
}
