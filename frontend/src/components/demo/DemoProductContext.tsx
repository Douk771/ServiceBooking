import { createContext, useContext, type ReactNode } from 'react'
import type { DemoProduct } from '../../api/demo'

const DemoProductContext = createContext<DemoProduct>('services')

/**
 * ARCHITECTURE_CYCLE35.md §35.7.3 — which product's demo this front end is. The product is a property of the build
 * (ezbook = `services`, goods = `orders`), not of the page address: the host is an unreliable signal (tests, proxies,
 * local development). Without a provider everything behaves as before cycle 35 (`services`).
 */
export function DemoProductProvider({ product, children }: { product: DemoProduct; children: ReactNode }) {
  return <DemoProductContext.Provider value={product}>{children}</DemoProductContext.Provider>
}

// Provider and hook share one file by design (ARCHITECTURE_CYCLE35.md §35.7.3); the context is never hot-swapped.
// eslint-disable-next-line react-refresh/only-export-components
export function useDemoProduct(): DemoProduct {
  return useContext(DemoProductContext)
}
