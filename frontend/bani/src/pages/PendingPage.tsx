/**
 * Stand-in for a screen that a later task of cycle 42 owns (FE-42-3 catalog and company, FE-42-4 resource, booking and
 * «Мои брони», FE-42-5 cabinet). The route table is complete from FE-42-2 so baniRoutes.test.ts guards it; each owner
 * replaces the element in BaniApp.tsx with the real page and deletes the usage here.
 */
export function PendingPage({ title }: { title: string }) {
  return (
    <main className="max-w-[560px] mx-auto px-4 py-24 text-center">
      <h1 className="font-serif text-2xl text-ink">{title}</h1>
      <p className="mt-2 text-sm text-ink-soft">Раздел готовится.</p>
    </main>
  )
}
