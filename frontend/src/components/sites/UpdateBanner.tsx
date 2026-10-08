import { Button } from '@/components/ui/Button'
import { useAppUpdate } from './useAppUpdate'

/** Offers a reload when a newer build is deployed; never reloads by itself, so a half-filled form is not lost. */
export function UpdateBanner() {
  const updateAvailable = useAppUpdate()
  if (!updateAvailable) return null
  return (
    <div
      role="status"
      className="fixed inset-x-3 bottom-3 z-50 mx-auto flex max-w-md items-center justify-between gap-3 rounded-2xl bg-ink px-4 py-3 text-sm text-white shadow-lg"
      style={{ marginBottom: 'env(safe-area-inset-bottom)' }}
    >
      <span>Доступна новая версия</span>
      <Button size="sm" onClick={() => window.location.reload()}>
        Обновить
      </Button>
    </div>
  )
}
