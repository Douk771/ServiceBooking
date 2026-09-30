import { create } from 'zustand'

/**
 * API_CONTRACT_CYCLE28.md §600a — while the demo is being reset every `/api/*` answers 503 + `X-Demo-Resetting: 1`.
 * `api/client.ts` flips this flag on the first such response so `DemoMaintenanceGate` can swap the whole app for the
 * "Демо обновляется" screen. In memory only: a reload starts from a clean slate (and asks `GET /api/demo/status`).
 */
interface DemoState {
  resetting: boolean
  setResetting: (value: boolean) => void
}

export const useDemoStore = create<DemoState>()((set) => ({
  resetting: false,
  setResetting: (value) => set({ resetting: value }),
}))
