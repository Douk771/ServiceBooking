import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { catalogApi } from '../../api/catalog'
import { useShopContext } from '../../hooks/useShop'
import { Button } from '@/components/ui/Button'
import { Icon } from '@/components/ui/Icon'
import { Modal } from '@/components/ui/Modal'
import { CategoryModal } from '../../components/catalog/CategoryModal'
import { ProductModal } from '../../components/catalog/ProductModal'
import { StockEditor } from '../../components/catalog/StockEditor'
import { SoldOutDialog } from '../../components/catalog/SoldOutDialog'
import { CategoryWeekdaysModal } from '../../components/catalog/CategoryWeekdaysModal'
import { EmptyState, ErrorState, InlineError, LoadingList } from '../../components/StatePanels'
import { filterProducts, groupProducts, moveItem, type CatalogSection } from '../../utils/catalogGroups'
import { formatUnitPrice } from '../../utils/quantityFormat'
import { getCatalogErrorMessage } from '../../utils/catalogError'
import type { CategoryDto, ProductDto, SoldOutScope } from '../../types'

/**
 * US-23-14/15/16/17. Owner: categories, products, order, photo. Staff (`Master`): only «закончилось» and the
 * stock — the edit controls are not rendered for them (the API answers 403 to them anyway, §392.2).
 */
export function CatalogPage() {
  const { shop, isOwner } = useShopContext()
  const qc = useQueryClient()
  const catKey = ['shop-categories', shop.id]
  const prodKey = ['shop-products', shop.id]
  const categories = useQuery({ queryKey: catKey, queryFn: () => catalogApi.categories(shop.id) })
  const products = useQuery({ queryKey: prodKey, queryFn: () => catalogApi.products(shop.id) })

  const [search, setSearch] = useState('')
  const [categoryModal, setCategoryModal] = useState<{ category?: CategoryDto } | null>(null)
  const [productModal, setProductModal] = useState<{ product?: ProductDto; categoryId?: string | null } | null>(null)
  const [deleting, setDeleting] = useState<{ kind: 'category'; category: CategoryDto } | { kind: 'product'; product: ProductDto } | null>(null)
  const [soldOutFor, setSoldOutFor] = useState<ProductDto | null>(null)
  const [weekdaysFor, setWeekdaysFor] = useState<CategoryDto | null>(null)
  const [actionError, setActionError] = useState('')

  const refreshAll = () => {
    void qc.invalidateQueries({ queryKey: catKey })
    void qc.invalidateQueries({ queryKey: prodKey })
    void qc.invalidateQueries({ queryKey: ['shop', shop.id] })
  }
  const replaceProduct = (p: ProductDto) =>
    qc.setQueryData<ProductDto[]>(prodKey, (old) => old?.map((x) => (x.id === p.id ? p : x)) ?? old)

  const soldOut = useMutation({
    mutationFn: (v: { p: ProductDto; value: boolean; scope?: SoldOutScope }) => catalogApi.setSoldOut(shop.id, v.p.id, v.value, v.scope),
    onMutate: () => setActionError(''),
    onSuccess: (p) => {
      replaceProduct(p)
      setSoldOutFor(null)
    },
    onError: (e) => {
      setSoldOutFor(null)
      setActionError(getCatalogErrorMessage(e, 'Не удалось изменить отметку «закончилось».'))
    },
  })
  const reorderCats = useMutation({
    mutationFn: (ids: string[]) => catalogApi.reorderCategories(shop.id, ids),
    onMutate: () => setActionError(''),
    onSuccess: () => void qc.invalidateQueries({ queryKey: catKey }),
    onError: (e) => {
      setActionError(getCatalogErrorMessage(e, 'Не удалось изменить порядок.'))
      refreshAll()
    },
  })
  const reorderProds = useMutation({
    mutationFn: (v: { categoryId: string | null; ids: string[] }) => catalogApi.reorderProducts(shop.id, v.categoryId, v.ids),
    onMutate: () => setActionError(''),
    onSuccess: () => void qc.invalidateQueries({ queryKey: prodKey }),
    onError: (e) => {
      setActionError(getCatalogErrorMessage(e, 'Не удалось изменить порядок.'))
      refreshAll()
    },
  })
  const doDelete = useMutation({
    mutationFn: async () => {
      if (!deleting) return
      if (deleting.kind === 'category') await catalogApi.deleteCategory(shop.id, deleting.category.id)
      else await catalogApi.deleteProduct(shop.id, deleting.product.id)
    },
    onSuccess: () => {
      setDeleting(null)
      refreshAll()
    },
  })

  const sections: CatalogSection[] = useMemo(() => {
    if (!categories.data || !products.data) return []
    const filtered = filterProducts(products.data, search)
    const all = groupProducts(categories.data, filtered)
    // While searching, hide categories with no match (the point of a search is finding a product).
    return search.trim() ? all.filter((s) => s.products.length > 0) : all
  }, [categories.data, products.data, search])

  const loading = categories.isLoading || products.isLoading
  const failed = categories.isError || products.isError
  const isEmpty = !loading && !failed && (categories.data?.length ?? 0) === 0 && (products.data?.length ?? 0) === 0

  return (
    <main className="max-w-[980px] mx-auto px-4 sm:px-8 pt-8">
      <div className="flex items-center justify-between gap-3 flex-wrap mb-5">
        <div className="relative w-full sm:w-72">
          <Icon name="search" size={15} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted" />
          <input
            type="search"
            aria-label="Поиск товара"
            placeholder="Найти товар"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full rounded-full border border-line bg-white pl-10 pr-4 py-2.5 text-sm outline-none focus:border-gold"
          />
        </div>
        {isOwner && (
          <div className="flex gap-2">
            <Button variant="secondary" onClick={() => setCategoryModal({})}>
              <Icon name="plus" size={15} /> Категория
            </Button>
            <Button onClick={() => setProductModal({})}>
              <Icon name="plus" size={15} /> Товар
            </Button>
          </div>
        )}
      </div>

      {isOwner && products.data && (
        <p className="text-sm text-ink-soft mb-4" data-testid="product-limit">
          Товаров: {products.data.length}{shop.productLimit ? ` из ${shop.productLimit}` : ''}
        </p>
      )}
      {!isOwner && <p className="text-sm text-ink-soft mb-4">Вы можете отмечать «закончилось» и править остатки. Товары и цены меняет владелец.</p>}
      {actionError && <div className="mb-4"><InlineError>{actionError}</InlineError></div>}

      {loading ? (
        <LoadingList rows={4} rowClass="h-24" />
      ) : failed ? (
        <ErrorState
          message={getCatalogErrorMessage(categories.error ?? products.error, 'Не удалось загрузить каталог.')}
          onRetry={() => {
            void categories.refetch()
            void products.refetch()
          }}
        />
      ) : isEmpty ? (
        <EmptyState
          title="Каталог пуст"
          text={isOwner ? 'Заведите категории и товары — покупатели увидят их на странице магазина.' : 'Владелец ещё не добавил товары.'}
          action={isOwner ? <Button onClick={() => setProductModal({})}>Добавить товар</Button> : undefined}
        />
      ) : sections.length === 0 ? (
        <p className="text-sm text-ink-soft py-8 text-center">По запросу «{search}» ничего не найдено.</p>
      ) : (
        <div className="flex flex-col gap-8">
          {sections.map((section) => {
            const cat = section.category
            const sortedCats = [...(categories.data ?? [])].sort((a, b) => a.position - b.position)
            const sortedIndex = cat ? sortedCats.findIndex((c) => c.id === cat.id) : -1
            return (
              <section key={cat?.id ?? 'other'} aria-label={cat?.name ?? 'Другое'}>
                <div className="flex items-center justify-between gap-3 mb-3">
                  <div className="flex items-center gap-2 min-w-0">
                    <h2 className="font-serif text-xl text-ink truncate">{cat?.name ?? 'Другое'}</h2>
                    {cat?.isHidden && (
                      <span className="inline-flex items-center gap-1 text-xs font-semibold px-2.5 py-1 rounded-full bg-cream-deep text-muted">
                        <Icon name="eye-off" size={12} /> Скрыта
                      </span>
                    )}
                    {!cat && <span className="text-xs text-muted">товары без категории</span>}
                  </div>
                  {isOwner && (
                    <div className="flex items-center gap-1 shrink-0">
                      {cat && (
                        <>
                          <IconButton label={`Поднять категорию ${cat.name}`} icon="arrow-up" disabled={sortedIndex <= 0 || reorderCats.isPending} onClick={() => reorderCats.mutate(moveItem(sortedCats, sortedIndex, -1).map((c) => c.id))} />
                          <IconButton label={`Опустить категорию ${cat.name}`} icon="arrow-down" disabled={sortedIndex >= sortedCats.length - 1 || reorderCats.isPending} onClick={() => reorderCats.mutate(moveItem(sortedCats, sortedIndex, 1).map((c) => c.id))} />
                          <Button variant="ghost" size="sm" aria-label={`Дни продажи категории ${cat.name}`} onClick={() => setWeekdaysFor(cat)}>
                            <Icon name="calendar" size={14} /> Дни
                          </Button>
                          <IconButton label={`Изменить категорию ${cat.name}`} icon="pencil" onClick={() => setCategoryModal({ category: cat })} />
                          <IconButton label={`Удалить категорию ${cat.name}`} icon="trash" onClick={() => { doDelete.reset(); setDeleting({ kind: 'category', category: cat }) }} />
                        </>
                      )}
                      <Button variant="ghost" size="sm" onClick={() => setProductModal({ categoryId: cat?.id ?? null })}>
                        <Icon name="plus" size={14} /> Товар
                      </Button>
                    </div>
                  )}
                </div>

                {section.products.length === 0 ? (
                  <p className="text-sm text-muted rounded-xl border border-dashed border-line-strong px-4 py-5">В категории пока нет товаров.</p>
                ) : (
                  <ul className="flex flex-col gap-2">
                    {section.products.map((p, i) => (
                      <ProductRow
                        key={p.id}
                        product={p}
                        shopId={shop.id}
                        trackStock={shop.settings.trackStock}
                        isOwner={isOwner}
                        canMoveUp={i > 0 && !search.trim()}
                        canMoveDown={i < section.products.length - 1 && !search.trim()}
                        onMove={(delta) => reorderProds.mutate({ categoryId: cat?.id ?? null, ids: moveItem(section.products, i, delta).map((x) => x.id) })}
                        onToggleSoldOut={() => (p.isSoldOut ? soldOut.mutate({ p, value: false }) : setSoldOutFor(p))}
                        togglePending={soldOut.isPending && soldOut.variables?.p.id === p.id}
                        onEdit={() => setProductModal({ product: p })}
                        onDelete={() => { doDelete.reset(); setDeleting({ kind: 'product', product: p }) }}
                        onStockChanged={replaceProduct}
                      />
                    ))}
                  </ul>
                )}
              </section>
            )
          })}
        </div>
      )}

      {categoryModal && (
        <CategoryModal
          shopId={shop.id}
          category={categoryModal.category}
          onClose={() => setCategoryModal(null)}
          onSaved={() => {
            setCategoryModal(null)
            refreshAll()
          }}
        />
      )}
      {productModal && (
        <ProductModal
          shopId={shop.id}
          categories={[...(categories.data ?? [])].sort((a, b) => a.position - b.position)}
          product={productModal.product}
          defaultCategoryId={productModal.categoryId}
          onClose={() => setProductModal(null)}
          onSaved={refreshAll}
        />
      )}
      {soldOutFor && (
        <SoldOutDialog product={soldOutFor} busy={soldOut.isPending} onClose={() => setSoldOutFor(null)} onConfirm={(scope) => soldOut.mutate({ p: soldOutFor, value: true, scope })} />
      )}
      {weekdaysFor && (
        <CategoryWeekdaysModal
          shopId={shop.id}
          category={weekdaysFor}
          onClose={() => setWeekdaysFor(null)}
          onSaved={() => {
            setWeekdaysFor(null)
            refreshAll()
          }}
        />
      )}
      {deleting && (
        <Modal
          title={deleting.kind === 'category' ? 'Удалить категорию?' : 'Удалить товар?'}
          onClose={() => setDeleting(null)}
          dismissible={!doDelete.isPending}
        >
          <p className="text-sm text-ink-soft">
            {deleting.kind === 'category'
              ? `Категория «${deleting.category.name}» будет удалена. Если в ней есть товары, сначала перенесите или удалите их.`
              : `Товар «${deleting.product.name}» исчезнет из каталога и со страницы магазина. Уже созданные заказы не изменятся.`}
          </p>
          {doDelete.isError && <div className="mt-3"><InlineError>{getCatalogErrorMessage(doDelete.error, 'Не удалось удалить.')}</InlineError></div>}
          <div className="flex gap-3 mt-5">
            <Button variant="secondary" className="flex-1" onClick={() => setDeleting(null)} disabled={doDelete.isPending}>Отмена</Button>
            <Button variant="danger" className="flex-1" loading={doDelete.isPending} onClick={() => doDelete.mutate()}>Удалить</Button>
          </div>
        </Modal>
      )}
    </main>
  )
}

function IconButton({ label, icon, onClick, disabled }: { label: string; icon: Parameters<typeof Icon>[0]['name']; onClick: () => void; disabled?: boolean }) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onClick}
      className="w-8 h-8 rounded-full flex items-center justify-center text-ink-soft hover:bg-cream-deep disabled:opacity-30 disabled:pointer-events-none transition-colors"
    >
      <Icon name={icon} size={15} />
    </button>
  )
}

interface RowProps {
  product: ProductDto
  shopId: string
  trackStock: boolean
  isOwner: boolean
  canMoveUp: boolean
  canMoveDown: boolean
  onMove: (delta: -1 | 1) => void
  onToggleSoldOut: () => void
  togglePending: boolean
  onEdit: () => void
  onDelete: () => void
  onStockChanged: (p: ProductDto) => void
}

function ProductRow({ product: p, shopId, trackStock, isOwner, canMoveUp, canMoveDown, onMove, onToggleSoldOut, togglePending, onEdit, onDelete, onStockChanged }: RowProps) {
  return (
    <li className="rounded-2xl border border-line bg-white p-4 flex gap-4">
      {p.thumbnailUrl ? (
        <img src={p.thumbnailUrl} alt="" className="w-16 h-16 rounded-xl object-cover shrink-0" />
      ) : (
        <span className="w-16 h-16 rounded-xl bg-cream-deep flex items-center justify-center text-muted shrink-0">
          <Icon name="image" size={22} strokeWidth={1.4} />
        </span>
      )}
      <div className="min-w-0 flex-1">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="font-semibold text-ink truncate">{p.name}</p>
            <p className="text-sm text-ink-soft">
              {formatUnitPrice(p.unit, p.price)}
              {p.unit === 'Weight' ? ' · весовой' : p.portionText ? ` · ${p.portionText}` : ''}
            </p>
            <div className="flex flex-wrap gap-1.5 mt-1.5">
              {!p.isPublished && <span className="text-xs font-semibold px-2.5 py-0.5 rounded-full bg-cream-deep text-muted">Не опубликован</span>}
              {p.isSoldOut && <span className="text-xs font-semibold px-2.5 py-0.5 rounded-full bg-warning-bg text-warning" data-testid="sold-out-chip">{p.soldOut?.text ? `Закончилось · ${p.soldOut.text}` : 'Закончилось'}</span>}
              {p.weekdaysLabel && <span className="text-xs font-semibold px-2.5 py-0.5 rounded-full bg-cream-deep text-ink-soft" data-testid="weekdays-chip">{p.weekdaysLabel}</span>}
              {!p.availableToCustomers && !p.isSoldOut && p.isPublished && <span className="text-xs font-semibold px-2.5 py-0.5 rounded-full bg-warning-bg text-warning">Покупателю недоступен</span>}
            </div>
          </div>
          <div className="flex items-center gap-1 shrink-0">
            {isOwner && (
              <>
                <IconButton label={`Поднять товар ${p.name}`} icon="arrow-up" disabled={!canMoveUp} onClick={() => onMove(-1)} />
                <IconButton label={`Опустить товар ${p.name}`} icon="arrow-down" disabled={!canMoveDown} onClick={() => onMove(1)} />
                <IconButton label={`Изменить товар ${p.name}`} icon="pencil" onClick={onEdit} />
                <IconButton label={`Удалить товар ${p.name}`} icon="trash" onClick={onDelete} />
              </>
            )}
          </div>
        </div>
        <div className="mt-3 flex flex-wrap items-center gap-x-5 gap-y-2">
          <Button variant={p.isSoldOut ? 'primary' : 'secondary'} size="sm" loading={togglePending} onClick={onToggleSoldOut} aria-pressed={p.isSoldOut}>
            {p.isSoldOut ? 'Вернуть в продажу' : 'Закончилось'}
          </Button>
          {trackStock && <StockEditor shopId={shopId} product={p} onChanged={onStockChanged} />}
        </div>
      </div>
    </li>
  )
}
