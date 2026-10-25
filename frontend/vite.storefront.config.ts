import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'

// ARCHITECTURE_CYCLE48_STOREFRONT.md §48.10.2 — SSR build of the storefront prerender (only `--ssr`):
//   vite build --config vite.storefront.config.ts --ssr  ->  dist-storefront-ssr/prerender.js (renderNoscript, renderPage)
// scripts/prerender-storefront.mjs loads it and writes <noscript> and contacts/ + payment-info/ into dist, dist-goods, dist-dom, dist-bani.
// No React plugin: the JSX is compiled by esbuild (tsconfig `jsx: react-jsx`), there is no client code here.
export default defineConfig({
  envDir: fileURLToPath(new URL('..', import.meta.url)),
  publicDir: false,
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  build: {
    outDir: 'dist-storefront-ssr',
    emptyOutDir: true,
    rollupOptions: {
      input: fileURLToPath(new URL('./src/storefront/prerender.tsx', import.meta.url)),
      output: { entryFileNames: 'prerender.js' },
    },
  },
})
