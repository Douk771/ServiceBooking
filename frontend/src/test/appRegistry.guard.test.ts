// @vitest-environment node
// CY44-06 (ARCHITECTURE_CYCLE44_PRODUCTS_LANDING.md §44.6.2): every frontend app of contracts/cycle44/frontend-apps.json is entered
// in every registration place (§44.6.1, items 5-14). A forgotten place turns CI red instead of surfacing in a release.
import { describe, it, expect } from 'vitest'
import registry from '../../../contracts/cycle44/frontend-apps.json'
import testAreas from '../../../contracts/cycle36/test-areas.json'
import eslintConfig from '../../eslint.config.js?raw'
import vitestConfig from '../../vitest.config.ts?raw'
import packageJson from '../../package.json?raw'
import gitignore from '../../../.gitignore?raw'
import testAreaScript from '../../scripts/test-area.mjs?raw'
import testAreasGuard from './testAreas.guard.test.ts?raw'
import ciYml from '../../../.github/workflows/ci.yml?raw'
import smokeScript from '../../../deploy/ci/smoke-frontend.sh?raw'
import deployRemote from '../../../deploy/deploy-remote.sh?raw'

const vhosts = import.meta.glob('../../../deploy/nginx/*.conf', { query: '?raw', import: 'default', eager: true }) as Record<string, string>
const vhost = (file: string) => Object.entries(vhosts).find(([k]) => k.endsWith(`/${file}`))?.[1]

/** Failure message names the missing needle instead of dumping the whole file. */
function has(text: string | undefined, needle: string, where = ''): void {
  expect(text?.includes(needle) ?? false, `missing ${where ? where + ': ' : ''}${needle}`).toBe(true)
}

const apps = registry.apps.filter((a) => a.distSubdir !== '')
const scripts = JSON.parse(packageJson).scripts as Record<string, string>

describe('CY44-06 app registry', () => {
  it('registry lists the five apps with unique names and ports', () => {
    expect(registry.apps.map((a) => a.name)).toEqual(['ezbook', 'goods', 'dom', 'bani', 'about'])
    expect(new Set(registry.apps.map((a) => a.devPort)).size).toBe(registry.apps.length)
  })

  describe.each(apps.map((a) => [a.name, a] as const))('%s is registered everywhere', (name, app) => {
    const U = name.toUpperCase()

    it('eslint: dist ignores' + (app.serviceWorker ? ' and service worker globals' : ''), () => {
      has(eslintConfig, `'dist-${name}/**'`)
      if (app.serviceWorker) has(eslintConfig, `'${name}/public/sw.js'`)
    })

    it('vitest: alias and include', () => {
      has(vitestConfig, `'@${name}'`)
      has(vitestConfig, `'${name}/src/**/*.test.{ts,tsx}'`)
    })

    it('package.json: dev/build scripts and build:release', () => {
      expect(scripts[`dev:${name}`]).toContain(`vite.${name}.config.ts`)
      expect(scripts[`build:${name}`]).toBeTruthy()
      expect(scripts['build:release']).toContain(`npm run build:${name}`)
      expect(scripts['build:release']).toMatch(new RegExp(`merge-site-dist\\.mjs[^&]*\\b${name}\\b`))
    })

    it('.gitignore: dist-' + name, () => {
      has(gitignore, `frontend/dist-${name}/`)
    })

    it('test areas: script, json prefix, guard glob', () => {
      has(testAreaScript, `'${name}/src'`)
      expect(testAreas.areas.flatMap((a) => a.frontendPathPrefixes)).toContain(`${name}/src/`)
      has(testAreasGuard, `../../${name}/src/**`)
    })

    it('ci.yml: type-check, smoke step' + (app.serviceWorker ? ' and sw.js check' : ''), () => {
      has(ciYml, `tsconfig.${name}.json`)
      has(ciYml, `DIST_DIR: frontend/dist/${app.distSubdir}`)
      has(ciYml, `SMOKE_PROFILE: ${app.smokeProfile}`)
      if (app.serviceWorker) has(ciYml, `${name}/public/sw.js`)
    })

    it('smoke-frontend.sh knows the profile', () => {
      has(smokeScript, `"${app.smokeProfile}"`)
    })

    it('deploy-remote.sh: host, smoke switch and vhost variables', () => {
      for (const v of app.deploySmoke === 'if-vhost-installed' ? ['HOST', 'SMOKE', 'VHOST'] : ['HOST', 'SMOKE']) has(deployRemote, `${U}_${v}=`)
      has(deployRemote, `${app.distSubdir}/index.html`)
      has(deployRemote, app.vhost)
    })

    it('nginx: own vhost serves the subdir; root vhosts answer 404 for it', () => {
      const own = vhost(app.vhost)
      expect(own, `deploy/nginx/${app.vhost} is missing`).toBeDefined()
      has(own, `server_name ${app.host}`)
      has(own, `current/${app.distSubdir}`)
      if (!app.apiProxy) expect(own).not.toMatch(/proxy_pass/)
      for (const root of registry.rootVhostsBlockingSubdirs) {
        has(vhost(root), `location ^~ /${app.distSubdir}/ { return 404; }`, root)
      }
    })
  })
})
