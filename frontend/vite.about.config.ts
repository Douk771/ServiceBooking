import { fileURLToPath } from 'node:url'
import { defineConfig, loadEnv, type Plugin, type ViteDevServer } from 'vite'
import tailwindcss from 'tailwindcss'
import autoprefixer from 'autoprefixer'

// about.ezbook.ru (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.4, §44.6.1). The only app of the package with NO client JavaScript:
//   build            -> dist-about/ (HTML template with markers + assets/*.css)
//   build --ssr      -> dist-about-ssr/prerender.js (Node module with renderSite)
//   scripts/prerender-about.mjs fills the template and writes index.html, 404.html, robots.txt, sitemap.xml.
// Dev (`npm run dev:about`): the plugin below renders the same renderSite on every request; no /api proxy.
// @vitejs/plugin-react is deliberately NOT used: there is no client React.
interface RenderModule {
  renderSite: (input: {
    template: string
    env: Record<string, string>
    mode: 'development'
    year: number
  }) => { files: Record<string, string> }
}

function aboutDevRender(envFor: () => Record<string, string>): Plugin {
  let server: ViteDevServer
  const render = async (template: string) => {
    const mod = (await server.ssrLoadModule('/src/prerender.tsx')) as RenderModule
    return mod.renderSite({ template, env: envFor(), mode: 'development', year: new Date().getFullYear() }).files
  }
  return {
    name: 'about-dev-render',
    apply: 'serve',
    configureServer(s) {
      server = s
      s.middlewares.use(async (req, res, next) => {
        const url = (req.url ?? '').split('?')[0]
        if (url !== '/robots.txt' && url !== '/sitemap.xml') return next()
        try {
          const files = await render('<!--about:head--><!--about:body-->')
          res.setHeader('Content-Type', url === '/robots.txt' ? 'text/plain; charset=utf-8' : 'application/xml; charset=utf-8')
          res.end(files[url.slice(1)])
        } catch (e) {
          next(e)
        }
      })
    },
    async transformIndexHtml(html) {
      return (await render(html))['index.html']
    },
    handleHotUpdate({ file, server: s }) {
      if (/\/about\/src\/|\/src\/utils\/publicSites\.ts$/.test(file)) {
        s.moduleGraph.invalidateAll()
        s.ws.send({ type: 'full-reload' })
        return []
      }
    },
  }
}

export default defineConfig(({ mode, isSsrBuild }) => {
  const frontendRoot = fileURLToPath(new URL('.', import.meta.url))
  const repoRoot = fileURLToPath(new URL('..', import.meta.url))
  const fileEnv = loadEnv(mode, repoRoot, ['VITE_', 'SB_'])
  const parsePort = (value: string | undefined, fallback: number): number => {
    const parsed = Number(value)
    return value !== undefined && Number.isInteger(parsed) && parsed >= 1 && parsed <= 65535 ? parsed : fallback
  }
  const webPort = parsePort(process.env.SB_ABOUT_WEB_PORT?.trim() || fileEnv.SB_ABOUT_WEB_PORT, 5177)
  const devEnv = () => ({ ...fileEnv, ...(process.env as Record<string, string>) })

  return {
    root: 'about',
    publicDir: 'public',
    envDir: repoRoot,
    plugins: [aboutDevRender(devEnv)],
    resolve: {
      alias: {
        '@about': fileURLToPath(new URL('./about/src', import.meta.url)),
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    css: {
      postcss: {
        plugins: [tailwindcss({ config: `${frontendRoot}tailwind.about.config.js` }), autoprefixer()],
      },
    },
    build: isSsrBuild
      ? {
          outDir: '../dist-about-ssr',
          emptyOutDir: true,
          rollupOptions: {
            input: fileURLToPath(new URL('./about/src/prerender.tsx', import.meta.url)),
            output: { entryFileNames: 'prerender.js' },
          },
        }
      : { outDir: '../dist-about', emptyOutDir: true },
    server: { port: webPort, strictPort: false },
  }
})
