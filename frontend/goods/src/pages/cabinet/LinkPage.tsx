import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { shopsApi } from '../../api/shops'
import { useShopContext } from '../../hooks/useShop'
import { useObjectUrl } from '../../hooks/useObjectUrl'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Icon } from '@/components/ui/Icon'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { useDebouncedValue } from '@/hooks/useDebouncedValue'
import { ErrorState, InlineError, Skeleton } from '../../components/StatePanels'
import { getCatalogErrorMessage } from '../../utils/catalogError'
import { isSlugFormatValid, normalizeSlugInput } from '../../utils/slug'

/** US-23-12 — link, «Скопировать», QR preview/download, and (owner only) changing the address. */
export function LinkPage() {
  const { shop, isOwner } = useShopContext()
  const qc = useQueryClient()
  const [copied, setCopied] = useState(false)
  const [copyFailed, setCopyFailed] = useState(false)
  const [editing, setEditing] = useState(false)
  const [newSlug, setNewSlug] = useState(shop.slug)
  const [confirming, setConfirming] = useState(false)
  const debouncedSlug = useDebouncedValue(newSlug.trim(), 500)

  const qr = useQuery({ queryKey: ['shop-qr', shop.id, shop.slug], queryFn: () => shopsApi.qr(shop.id), staleTime: Infinity, retry: 1 })
  const qrUrl = useObjectUrl(qr.data)

  const formatOk = debouncedSlug === '' || isSlugFormatValid(debouncedSlug)
  const changed = debouncedSlug !== '' && debouncedSlug !== shop.slug
  const check = useQuery({
    queryKey: ['slug-check', debouncedSlug],
    queryFn: () => shopsApi.slugCheck({ slug: debouncedSlug }),
    enabled: editing && changed && formatOk,
    retry: false,
  })

  const change = useMutation({
    mutationFn: () => shopsApi.updateSlug(shop.id, newSlug.trim()),
    onSuccess: (updated) => {
      qc.setQueryData(['shop', shop.id], updated)
      void qc.invalidateQueries({ queryKey: ['my-shops'] })
      setConfirming(false)
      setEditing(false)
    },
    onError: () => setConfirming(false),
  })

  const copy = async () => {
    setCopyFailed(false)
    try {
      await navigator.clipboard.writeText(shop.publicUrl)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard denied (non-secure context, permissions) — the link stays selectable as text.
      setCopyFailed(true)
    }
  }

  const hint = !formatOk
    ? { ok: false, text: 'Адрес — латиница, цифры и дефис, от 3 до 50 символов' }
    : changed && check.data
      ? check.data.available
        ? { ok: true, text: 'Адрес свободен' }
        : { ok: false, text: check.data.reason ?? 'Этот адрес недоступен' }
      : null

  return (
    <main className="max-w-[860px] mx-auto px-4 sm:px-8 pt-8 grid gap-5 md:grid-cols-[1fr_280px] items-start">
      <div className="flex flex-col gap-5">
        <Card className="p-6">
          <h2 className="text-[15px] font-semibold text-ink mb-3">Ссылка на магазин</h2>
          <p className="rounded-xl bg-cream-deep px-4 py-3 text-sm text-ink break-all select-all" data-testid="shop-public-url">
            {shop.publicUrl}
          </p>
          <div className="mt-3 flex items-center gap-3 flex-wrap">
            <Button onClick={() => void copy()} variant="secondary">
              <Icon name={copied ? 'check' : 'copy'} size={15} />
              {copied ? 'Скопировано' : 'Скопировать'}
            </Button>
            <a href={shop.publicUrl} target="_blank" rel="noreferrer" className="text-sm font-medium text-gold hover:text-gold-dark inline-flex items-center gap-1.5">
              Открыть <Icon name="external-link" size={14} />
            </a>
          </div>
          {copyFailed && <p className="text-xs text-muted mt-2">Не удалось скопировать автоматически — выделите ссылку и скопируйте вручную.</p>}
        </Card>

        {isOwner && (
          <Card className="p-6">
            <h2 className="text-[15px] font-semibold text-ink mb-1">Адрес магазина</h2>
            {!editing ? (
              <>
                <p className="text-sm text-ink-soft mb-3">
                  Сейчас: <span className="text-ink font-medium">{shop.slug}</span>
                </p>
                <Button variant="secondary" size="sm" onClick={() => { setNewSlug(shop.slug); setEditing(true) }}>
                  Изменить адрес
                </Button>
              </>
            ) : (
              <div className="flex flex-col gap-3">
                <p className="text-sm text-warning bg-warning-bg rounded-xl px-4 py-3">
                  Старая ссылка и напечатанные QR-коды перестанут работать. Перенаправления со старого адреса нет.
                </p>
                <Input
                  label="Новый адрес"
                  value={newSlug}
                  maxLength={50}
                  autoCapitalize="none"
                  spellCheck={false}
                  onChange={(e) => setNewSlug(normalizeSlugInput(e.target.value))}
                />
                {hint && (
                  <p role="status" className={`text-xs font-medium ${hint.ok ? 'text-success' : 'text-danger'}`}>
                    {hint.text}
                  </p>
                )}
                {change.isError && <InlineError>{getCatalogErrorMessage(change.error, 'Не удалось изменить адрес.')}</InlineError>}
                <div className="flex gap-3">
                  <Button variant="secondary" onClick={() => setEditing(false)}>
                    Отмена
                  </Button>
                  <Button onClick={() => setConfirming(true)} disabled={!changed || !formatOk || check.data?.available === false}>
                    Изменить адрес
                  </Button>
                </div>
              </div>
            )}
          </Card>
        )}
      </div>

      <Card className="p-6">
        <h2 className="text-[15px] font-semibold text-ink mb-3">QR-код</h2>
        {qr.isLoading ? (
          <Skeleton className="aspect-square w-full" />
        ) : qr.isError || !qrUrl ? (
          <ErrorState message="Не удалось загрузить QR-код." onRetry={() => void qr.refetch()} />
        ) : (
          <>
            <img src={qrUrl} alt={`QR-код магазина ${shop.name}`} className="w-full aspect-square rounded-xl border border-line bg-white" />
            <a
              href={qrUrl}
              download={`${shop.slug}-qr.png`}
              className="mt-3 inline-flex items-center gap-2 rounded-full bg-ink px-5 py-2.5 text-sm font-semibold !text-cream hover:bg-ink/90"
            >
              <Icon name="download" size={15} /> Скачать PNG
            </a>
            <p className="text-xs text-muted mt-2">Подходит для печати на A4 — разместите у кассы и на витрине.</p>
          </>
        )}
      </Card>

      {confirming && (
        <Modal title="Изменить адрес магазина?" onClose={() => setConfirming(false)} dismissible={!change.isPending}>
          <p className="text-sm text-ink-soft">
            Новая ссылка: <span className="text-ink font-medium break-all">goods.ezbook.ru/{newSlug.trim()}</span>. Старая ссылка и
            напечатанные QR-коды перестанут работать — их придётся заменить.
          </p>
          <div className="flex gap-3 mt-5">
            <Button variant="secondary" className="flex-1" onClick={() => setConfirming(false)} disabled={change.isPending}>
              Не менять
            </Button>
            <Button className="flex-1" loading={change.isPending} onClick={() => change.mutate()}>
              Да, изменить
            </Button>
          </div>
        </Modal>
      )}
    </main>
  )
}
