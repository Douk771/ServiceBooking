// @vitest-environment node
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { pushWorkerScriptUrl, refreshPushWorkerPeer, registerPushWorker } from './pushWorker'

describe('pushWorkerScriptUrl', () => {
  it('no peer → plain script', () => {
    expect(pushWorkerScriptUrl(null)).toBe('/sw.js')
    expect(pushWorkerScriptUrl(undefined)).toBe('/sw.js')
    expect(pushWorkerScriptUrl('')).toBe('/sw.js')
  })
  it('normalises to origin and encodes', () => {
    expect(pushWorkerScriptUrl('https://goods.ezbook.ru/some/path?x=1')).toBe('/sw.js?peer=https%3A%2F%2Fgoods.ezbook.ru')
    expect(pushWorkerScriptUrl('https://ezbook.ru/')).toBe('/sw.js?peer=https%3A%2F%2Fezbook.ru')
  })
  it('garbage → plain script', () => {
    expect(pushWorkerScriptUrl('not a url')).toBe('/sw.js')
  })
})

describe('registerPushWorker / refreshPushWorkerPeer', () => {
  const register = vi.fn()
  const getRegistration = vi.fn()

  beforeEach(() => {
    register.mockReset().mockResolvedValue({})
    getRegistration.mockReset()
    Object.defineProperty(globalThis.navigator, 'serviceWorker', { value: { register, getRegistration }, configurable: true })
  })
  afterEach(() => {
    Reflect.deleteProperty(globalThis.navigator, 'serviceWorker')
  })

  const reg = (scriptURL: string) => ({ active: { scriptURL } })

  it('without peer keeps the scriptURL of the active worker', async () => {
    getRegistration.mockResolvedValue(reg('https://goods.ezbook.ru/sw.js?peer=https%3A%2F%2Fezbook.ru'))
    await registerPushWorker()
    expect(register).toHaveBeenCalledWith('/sw.js?peer=https%3A%2F%2Fezbook.ru')
  })

  it('without peer and without registration registers the plain script', async () => {
    getRegistration.mockResolvedValue(undefined)
    await registerPushWorker()
    expect(register).toHaveBeenCalledWith('/sw.js')
  })

  it('with peer registers with it', async () => {
    await registerPushWorker('https://goods.ezbook.ru')
    expect(register).toHaveBeenCalledWith('/sw.js?peer=https%3A%2F%2Fgoods.ezbook.ru')
  })

  it('refresh does nothing when there is no registration', async () => {
    getRegistration.mockResolvedValue(undefined)
    await refreshPushWorkerPeer('https://goods.ezbook.ru')
    expect(register).not.toHaveBeenCalled()
  })

  it('refresh does nothing when the url already matches', async () => {
    getRegistration.mockResolvedValue(reg('https://ezbook.ru/sw.js?peer=https%3A%2F%2Fgoods.ezbook.ru'))
    await refreshPushWorkerPeer('https://goods.ezbook.ru')
    expect(register).not.toHaveBeenCalled()
  })

  it('refresh re-registers when the peer differs', async () => {
    getRegistration.mockResolvedValue(reg('https://ezbook.ru/sw.js'))
    await refreshPushWorkerPeer('https://goods.ezbook.ru')
    expect(register).toHaveBeenCalledWith('/sw.js?peer=https%3A%2F%2Fgoods.ezbook.ru')
  })
})
