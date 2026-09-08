import { Link } from 'react-router-dom'

/**
 * Site-wide footer. Its only job in this cycle is the pair of legal links required by US-36 п. 4 —
 * real `<a>`/`<Link>` elements, not onClick handlers, so crawlers and users without JS both see them
 * (ARCHITECTURE.md §4.5).
 */
export function Footer() {
  return (
    <footer className="border-t border-line mt-16">
      <div className="max-w-[1180px] mx-auto px-4 sm:px-8 py-6 flex flex-wrap items-center justify-center gap-x-6 gap-y-2 text-xs text-muted">
        <span>© {new Date().getFullYear()} EZBOOK</span>
        <Link to="/privacy" className="hover:text-ink-soft transition-colors">
          Политика обработки персональных данных
        </Link>
        <Link to="/terms" className="hover:text-ink-soft transition-colors">
          Пользовательское соглашение
        </Link>
      </div>
    </footer>
  )
}
