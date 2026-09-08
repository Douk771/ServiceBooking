import { QueryClient } from '@tanstack/react-query'

// Extracted from App.tsx into its own module so src/api/client.ts can invalidate
// ['legal-consent-status'] the moment a 451 comes back (ARCHITECTURE.md §0.3), instead of waiting for
// whatever poll interval the query would otherwise use.
export const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 30_000 } },
})
