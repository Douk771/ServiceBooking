import { Link } from 'react-router-dom'
import { useAuthStore } from '../../store/authStore'
import { Icon } from '../ui/Icon'
import { BTN_PRIMARY, BTN_SECONDARY } from './classes'
import type { LandingAction } from './types'

/** Кнопка/ссылка по LandingAction (ARCHITECTURE_CYCLE38.md §38.3.5). Основная — тёмная со стрелкой. */
export function LandingActionLink({ action, primary }: { action: LandingAction; primary: boolean }) {
  const authed = useAuthStore((s) => s.isAuthenticated())
  const className = primary ? BTN_PRIMARY : BTN_SECONDARY
  const content = (
    <>
      {action.label}
      {primary && <Icon name="arrow-right" size={16} aria-hidden />}
    </>
  )
  if (action.kind === 'anchor') {
    return (
      <a href={action.href} className={className}>
        {content}
      </a>
    )
  }
  const to = action.kind === 'route' ? action.to : authed ? action.authedTo : action.guestTo
  return (
    <Link to={to} className={className}>
      {content}
    </Link>
  )
}
