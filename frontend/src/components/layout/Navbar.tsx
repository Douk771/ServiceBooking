import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { useAuthStore } from '../../store/authStore'
import { Icon } from '../ui/Icon'

export function Navbar() {
  const { user, logout, isAuthenticated, hasRole } = useAuthStore()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [menuOpen, setMenuOpen] = useState(false)

  const closeMenu = () => setMenuOpen(false)
  // Clears every cached query on logout — otherwise, on a shared computer, the next person to log
  // in sees the previous user's ['my-companies'], ['company-clients', …], reports etc. for the first
  // render, before their own queries refetch (US-19, C5).
  const handleLogout = () => {
    closeMenu()
    logout()
    qc.clear()
    navigate('/')
  }

  const isMasterOrOwner = hasRole('Master') || hasRole('CompanyOwner') || hasRole('SuperAdmin')
  const isAdmin = hasRole('SuperAdmin')
  // "Мои визиты" (personal bookings as a client) is shown to every authenticated user, not just
  // Client-only accounts: a master or owner can also be a client of a different salon (US-08, Q13).

  const navLinkClass = 'text-sm font-medium text-ink-soft hover:text-gold-dark transition-colors'
  const mobileLinkClass =
    'block px-4 py-3 text-[15px] font-medium text-ink-soft hover:text-gold-dark hover:bg-cream-deep rounded-xl transition-colors'

  return (
    <nav className="sticky top-0 z-50 bg-cream/86 backdrop-blur-md border-b border-line">
      <div className="max-w-[1180px] mx-auto px-4 sm:px-8 h-[76px] flex items-center justify-between">
        <Link to="/" className="flex items-center gap-3 shrink-0" onClick={closeMenu}>
          <span className="w-[38px] h-[38px] rounded-full bg-ink flex items-center justify-center shrink-0">
            <Icon name="calendar" size={18} className="text-cream" strokeWidth={1.6} />
          </span>
          <span className="font-serif text-xl text-ink">EZBOOK</span>
        </Link>

        {/* Desktop nav */}
        <div className="hidden md:flex items-center gap-7">
          {isAuthenticated() ? (
            <>
              {isMasterOrOwner && (
                <Link to="/my-bookings" className={navLinkClass}>
                  Мои записи
                </Link>
              )}
              <Link to="/my-visits" className={navLinkClass}>
                Мои визиты
              </Link>
              {isMasterOrOwner && (
                <Link to="/cabinet" className={navLinkClass}>
                  Кабинет
                </Link>
              )}
              {isAdmin && (
                <Link to="/admin" className={navLinkClass}>
                  Админ
                </Link>
              )}
              <button
                onClick={handleLogout}
                className="flex items-center gap-1.5 text-sm font-medium text-ink-soft hover:text-gold-dark transition-colors"
              >
                <Icon name="log-out" size={15} strokeWidth={1.7} />
                Выйти
              </button>
              <Link
                to="/profile"
                className="w-9 h-9 rounded-full bg-cream-deep flex items-center justify-center text-gold-dark font-bold text-xs hover:bg-line transition-colors shrink-0"
              >
                {user?.firstName[0]}
                {user?.lastName[0]}
              </Link>
            </>
          ) : (
            <>
              <Link to="/login" className={navLinkClass}>
                Войти
              </Link>
              <Link
                to="/register"
                className="inline-flex items-center bg-ink hover:bg-ink/90 text-cream text-sm font-semibold px-[22px] py-2.5 rounded-full transition-colors"
              >
                Регистрация
              </Link>
            </>
          )}
        </div>

        {/* Mobile controls */}
        <div className="flex items-center gap-2 md:hidden">
          {isAuthenticated() && (
            <Link
              to="/profile"
              className="w-9 h-9 rounded-full bg-cream-deep flex items-center justify-center text-gold-dark font-bold text-xs hover:bg-line transition-colors shrink-0"
              onClick={closeMenu}
            >
              {user?.firstName[0]}
              {user?.lastName[0]}
            </Link>
          )}
          <button
            onClick={() => setMenuOpen((v) => !v)}
            className="w-9 h-9 rounded-full flex items-center justify-center text-ink-soft hover:bg-cream-deep transition-colors shrink-0"
            aria-label={menuOpen ? 'Закрыть меню' : 'Открыть меню'}
          >
            <Icon name={menuOpen ? 'x' : 'menu'} size={20} strokeWidth={1.8} />
          </button>
        </div>
      </div>

      {/* Mobile dropdown */}
      {menuOpen && (
        <div className="md:hidden border-t border-line bg-cream px-4 py-3 flex flex-col gap-1">
          {isAuthenticated() ? (
            <>
              {isMasterOrOwner && (
                <Link to="/my-bookings" className={mobileLinkClass} onClick={closeMenu}>
                  Мои записи
                </Link>
              )}
              <Link to="/my-visits" className={mobileLinkClass} onClick={closeMenu}>
                Мои визиты
              </Link>
              {isMasterOrOwner && (
                <Link to="/cabinet" className={mobileLinkClass} onClick={closeMenu}>
                  Кабинет
                </Link>
              )}
              {isAdmin && (
                <Link to="/admin" className={mobileLinkClass} onClick={closeMenu}>
                  Админ
                </Link>
              )}
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
              <Link to="/register" className={mobileLinkClass} onClick={closeMenu}>
                Регистрация
              </Link>
            </>
          )}
        </div>
      )}
    </nav>
  )
}
