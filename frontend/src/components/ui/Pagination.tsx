import { Icon } from './Icon'

interface Props {
  page: number
  pageSize: number
  total: number
  hasNext: boolean
  onPageChange: (page: number) => void
}

/**
 * Shared pager for the four endpoints paginated in this cycle (API_CONTRACT.md §11) — one page-size
 * step per click, `hasNext` decides whether "next" is enabled rather than the frontend computing it
 * from `total` itself (the server already does that arithmetic).
 */
export function Pagination({ page, pageSize, total, hasNext, onPageChange }: Props) {
  if (total <= pageSize && page === 1) return null

  const from = total === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, total)

  return (
    <div className="flex items-center justify-between gap-3 mt-4 flex-wrap">
      <span className="text-xs text-muted">
        {from}–{to} из {total}
      </span>
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={() => onPageChange(page - 1)}
          disabled={page <= 1}
          aria-label="Предыдущая страница"
          className="w-8 h-8 rounded-full border border-line flex items-center justify-center text-ink-soft hover:border-line-strong disabled:opacity-40 disabled:cursor-not-allowed transition-all"
        >
          <Icon name="chevron-left" size={14} strokeWidth={1.8} />
        </button>
        <span className="text-xs text-muted min-w-[3rem] text-center">Стр. {page}</span>
        <button
          type="button"
          onClick={() => onPageChange(page + 1)}
          disabled={!hasNext}
          aria-label="Следующая страница"
          className="w-8 h-8 rounded-full border border-line flex items-center justify-center text-ink-soft hover:border-line-strong disabled:opacity-40 disabled:cursor-not-allowed transition-all"
        >
          <Icon name="chevron-right" size={14} strokeWidth={1.8} />
        </button>
      </div>
    </div>
  )
}
