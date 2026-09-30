import { useState } from 'react'
import { Link, NavLink, useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { useAuthStore } from '@/store/authStore'
import { Icon } from '@/components/ui/Icon'

/** "ezbook · Заказы" (SPEC US-23-04). Signed-in: «Мои заказы», «Кабинет», профиль, выход. */
export function GoodsNavbar() {
  const { user, logout, isAuthenticated } = useAuthStore()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [menuOpen, setMenuOpen] = useState(false)
  const closeMenu = () => setMenuOpen(false)
  const authed = isAuthenticated()

  // Same shared-computer rule as ezbook (US-19, C5): drop every cached query with the session. There is
  // no push subscription to remove on goods (no service worker in cycle 1).
  const handleLogout = () => {
    closeMenu()
    logout()
    qc.clear()
    navigate('/')
  }

  const linkClass = ({ isActive }: { isActive: boolean }) =>
    `text-sm font-medium transition-colors ${isActive ? 'text-ink' : 'text-ink-soft hover:text-gold-dark'}`
  const mobileLinkClass =
    'block px-4 py-3 text-[15px] font-medium text-ink-soft hover:text-gold-dark hover:bg-cream-deep rounded-xl transition-colors'

  return (
    <nav className="sticky top-0 z-40 bg-cream/90 backdrop-blur-md border-b border-line">
      <div className="max-w-[1180px] mx-auto px-4 sm:px-8 h-[68px] flex items-center justify-between">
        <Link to="/" onClick={closeMenu} className="flex items-center gap-3 shrink-0" aria-label="ezbook · Заказы">
          <span className="w-[38px] h-[38px] rounded-full bg-ink flex items-center justify-center shrink-0">
            <Icon name="shopping-bag" size={18} className="text-cream" strokeWidth={1.6} />
          </span>
          <span className="flex items-center gap-2.5">
            <span className="font-serif text-[22px] text-ink leading-none">ezbook</span>
            <span className="text-line-strong leading-none" aria-hidden="true">
              ·
            </span>
            <span className="text-[13px] font-semibold tracking-wide uppercase text-gold-dark leading-none">Заказы</span>
          </span>
        </Link>

        <div className="hidden md:flex items-center gap-7">
          {authed ? (
            <>
              <NavLink to="/orders" className={linkClass}>
                Мои заказы
              </NavLink>
              <NavLink to="/cabinet" className={linkClass}>
                Кабинет
              </NavLink>
              <button
                onClick={handleLogout}
                className="flex items-center gap-1.5 text-sm font-medium text-ink-soft hover:text-gold-dark transition-colors"
              >
                <Icon name="log-out" size={15} strokeWidth={1.7} />
                Выйти
              </button>
              <Link
                to="/profile"
                aria-label="Профиль"
                className="w-9 h-9 rounded-full bg-cream-deep flex items-center justify-center text-gold-dark font-bold text-xs hover:bg-line transition-colors shrink-0"
              >
                {user?.firstName?.[0]}
                {user?.lastName?.[0]}
              </Link>
            </>
          ) : (
            <>
              <Link to="/login" className="text-sm font-medium text-ink-soft hover:text-gold-dark transition-colors">
                Войти
              </Link>
              <Link
                to="/register?returnTo=%2Fcabinet%2Fnew"
                className="inline-flex items-center bg-ink hover:bg-ink/90 text-cream text-sm font-semibold px-[22px] py-2.5 rounded-full transition-colors"
              >
                Открыть магазин
              </Link>
            </>
          )}
        </div>

        <div className="flex items-center gap-2 md:hidden">
          {authed && (
            <Link
              to="/profile"
              aria-label="Профиль"
              onClick={closeMenu}
              className="w-9 h-9 rounded-full bg-cream-deep flex items-center justify-center text-gold-dark font-bold text-xs shrink-0"
            >
              {user?.firstName?.[0]}
              {user?.lastName?.[0]}
            </Link>
          )}
          <button
            onClick={() => setMenuOpen((v) => !v)}
            className="w-9 h-9 rounded-full flex items-center justify-center text-ink-soft hover:bg-cream-deep transition-colors shrink-0"
            aria-label={menuOpen ? 'Закрыть меню' : 'Открыть меню'}
            aria-expanded={menuOpen}
          >
            <Icon name={menuOpen ? 'x' : 'menu'} size={20} strokeWidth={1.8} />
          </button>
        </div>
      </div>

      {menuOpen && (
        <div className="md:hidden border-t border-line bg-cream px-4 py-3 flex flex-col gap-1">
          {authed ? (
            <>
              <Link to="/orders" className={mobileLinkClass} onClick={closeMenu}>
                Мои заказы
              </Link>
              <Link to="/cabinet" className={mobileLinkClass} onClick={closeMenu}>
                Кабинет
              </Link>
              <button onClick={handleLogout} className={`${mobileLinkClass} flex items-center gap-2 text-left w-full`}>
                <Icon name="log-out" size={16} strokeWidth={1.7} />
                Выйти
              </button>
            </>
          ) : (
            <>
              <Link to="/login" className={mobileLinkClass} onClick={closeMenu}>
                Войти
              </Link>
              <Link to="/register?returnTo=%2Fcabinet%2Fnew" className={mobileLinkClass} onClick={closeMenu}>
                Открыть магазин
              </Link>
            </>
          )}
        </div>
      )}
    </nav>
  )
}
