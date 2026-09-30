import { useState, type ComponentProps } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { demoApi, type DemoRole } from '../../api/demo'
import { useAuthStore } from '../../store/authStore'
import { useDemoStatus } from '../../hooks/useDemoStatus'
import { Button } from '../ui/Button'
import { Icon } from '../ui/Icon'
import { getAuthErrorMessage } from '../../utils/authError'
import { demoRoleHome } from '../../utils/demoRoles'

const ROLE_ICONS: Record<DemoRole, ComponentProps<typeof Icon>['name']> = {
  owner: 'store',
  master: 'scissors',
  client: 'user',
}

/**
 * API_CONTRACT_CYCLE28.md §598 — passwordless sign-in under one of three demo roles, on the login screen. Shown only
 * on the demo stand (`GET /api/demo/status` = 200); on production the block does not exist. The labels come from the
 * server (`roles[].label`), the route after signing in from the contract (owner → /cabinet, master → /my-bookings,
 * client → /my-visits).
 */
export function DemoRoleButtons() {
  const { status, isDemo } = useDemoStatus()
  const { setAuth } = useAuthStore()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [pending, setPending] = useState<DemoRole | null>(null)
  const [error, setError] = useState('')

  if (!isDemo || !status) return null

  const signIn = async (role: DemoRole) => {
    setPending(role)
    setError('')
    try {
      const res = await demoApi.login(role)
      // Same reasoning as the password sign-in: a previous user's cache must not show through for a frame.
      qc.removeQueries({ predicate: (q) => q.queryKey[0] !== 'demo-status' })
      setAuth(
        {
          id: res.userId,
          phone: res.phone,
          email: res.email,
          firstName: res.firstName,
          lastName: res.lastName,
          roles: res.roles,
        },
        res.token,
      )
      navigate(demoRoleHome(role))
    } catch (e: unknown) {
      // 400 / 409 ("Демо-данные ещё не созданы…") / 429 arrive as text/plain and are printed as is; 503 during a reset
      // is taken over by DemoMaintenanceGate.
      setError(getAuthErrorMessage(e))
      setPending(null)
    }
  }

  return (
    <section
      aria-labelledby="demo-login-title"
      data-testid="demo-role-buttons"
      className="mt-7 border-t border-line pt-6"
    >
      <h2
        id="demo-login-title"
        className="mb-1 text-center text-[13px] font-semibold uppercase tracking-wide text-ink-soft"
      >
        Демо: вход без пароля
      </h2>
      <div className="mt-4 flex flex-col gap-2.5">
        {status.roles.map((r) => (
          <Button
            key={r.role}
            type="button"
            variant="secondary"
            size="md"
            className="w-full justify-start"
            loading={pending === r.role}
            disabled={pending !== null}
            onClick={() => void signIn(r.role)}
          >
            {pending !== r.role && <Icon name={ROLE_ICONS[r.role]} size={16} strokeWidth={1.7} />}
            {r.label}
          </Button>
        ))}
      </div>
      {error && (
        <div role="alert" className="mt-3 rounded-xl bg-danger-bg px-4 py-2 text-sm text-danger">
          {error}
        </div>
      )}
    </section>
  )
}
