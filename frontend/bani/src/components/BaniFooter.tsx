import { Link } from 'react-router-dom'
import { bathsVertical } from '../vertical'

/** Legal links stay on this domain (real <Link>s to the bani routes of the shared legal pages). */
export function BaniFooter() {
  const link = 'hover:text-ink-soft transition-colors'
  return (
    <footer className="border-t border-line mt-16">
      <div className="max-w-[1180px] mx-auto px-4 sm:px-8 py-6 flex flex-wrap items-center justify-center gap-x-6 gap-y-2 text-xs text-muted">
        <span>
          © {new Date().getFullYear()} {bathsVertical.brand}
        </span>
        <Link to="/privacy" className={link}>
          Политика обработки персональных данных
        </Link>
        <Link to="/terms" className={link}>
          Пользовательское соглашение
        </Link>
        <Link to="/terms-owner" className={link}>
          Соглашение с компанией
        </Link>
        <Link to="/pdn-consent" className={link}>
          Согласие на обработку ПДн
        </Link>
        <Link to="/data-request" className={link}>
          Обращение по своим данным
        </Link>
      </div>
    </footer>
  )
}
