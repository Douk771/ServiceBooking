import { useCallback, useEffect, useState } from 'react'

export type SoundState = 'off' | 'on' | 'blocked' | 'unsupported'

type AudioCtor = typeof AudioContext

function audioContextCtor(): AudioCtor | undefined {
  const w = window as unknown as { AudioContext?: AudioCtor; webkitAudioContext?: AudioCtor }
  return w.AudioContext ?? w.webkitAudioContext
}

// The context and the «enabled» flag live at module level: the orders page unmounts when the shop owner switches
// to another tab (SPA navigation, no reload), and the browser's audio unlock would be lost with it.
let sharedCtx: AudioContext | null = null
let sharedEnabled = false

// The choice survives a reload (the audio unlock itself cannot: the browser wants a fresh gesture, see the gesture hook below).
const STORAGE_KEY = 'goods.newOrderSound'

function readWanted(): boolean {
  try {
    return window.localStorage.getItem(STORAGE_KEY) === '1'
  } catch {
    return false
  }
}

function writeWanted(on: boolean) {
  try {
    if (on) window.localStorage.setItem(STORAGE_KEY, '1')
    else window.localStorage.removeItem(STORAGE_KEY)
  } catch {
    // storage blocked — the choice just will not be remembered
  }
}

/** After a reload: build the (suspended) context from the remembered choice; the first tap anywhere resumes it. */
function restoreFromStorage() {
  const Ctor = audioContextCtor()
  if (!Ctor || sharedEnabled || !readWanted()) return
  sharedCtx = sharedCtx ?? new Ctor()
  sharedEnabled = true
}

function initialState(): SoundState {
  if (!audioContextCtor()) return 'unsupported'
  restoreFromStorage()
  if (!sharedEnabled || !sharedCtx) return 'off'
  return sharedCtx.state === 'running' ? 'on' : 'blocked'
}

/**
 * «Включить звук» (§397.3). Browsers refuse audio before a user gesture, so `enable()` must be called from a
 * click: it creates/resumes the AudioContext and plays a short test signal. The signal is generated (two tones,
 * ~0.6 s) — no audio files. The state is shown permanently: off, on, or blocked by the browser
 * (`AudioContext.state !== 'running'` after it was enabled).
 */
export function useNewOrderSound() {
  const [state, setState] = useState<SoundState>(initialState)

  const sync = useCallback(() => {
    const ctx = sharedCtx
    if (!sharedEnabled || !ctx) return
    setState(ctx.state === 'running' ? 'on' : 'blocked')
  }, [])

  const tone = (ctx: AudioContext, freq: number, start: number, dur: number) => {
    const osc = ctx.createOscillator()
    const gain = ctx.createGain()
    osc.type = 'sine'
    osc.frequency.value = freq
    gain.gain.setValueAtTime(0.0001, start)
    gain.gain.exponentialRampToValueAtTime(0.35, start + 0.03)
    gain.gain.exponentialRampToValueAtTime(0.0001, start + dur)
    osc.connect(gain).connect(ctx.destination)
    osc.start(start)
    osc.stop(start + dur + 0.02)
  }

  const play = useCallback(() => {
    const ctx = sharedCtx
    if (!sharedEnabled || !ctx) return
    if (ctx.state !== 'running') {
      // Suspended (tab was frozen, OS took the audio session): try to wake it; without a gesture it may stay blocked.
      void ctx.resume().finally(sync)
    }
    const t = ctx.currentTime
    tone(ctx, 880, t, 0.28)
    tone(ctx, 660, t + 0.3, 0.32)
  }, [sync])

  const enable = useCallback(async () => {
    const Ctor = audioContextCtor()
    if (!Ctor) {
      setState('unsupported')
      return
    }
    if (!sharedCtx) sharedCtx = new Ctor()
    sharedCtx.onstatechange = sync
    sharedEnabled = true
    writeWanted(true)
    try {
      await sharedCtx.resume()
    } catch {
      // stays blocked — reflected below
    }
    sync()
    play()
  }, [play, sync])

  const disable = useCallback(() => {
    sharedEnabled = false
    writeWanted(false)
    setState('off')
  }, [])

  useEffect(() => {
    // A remembered choice after a reload: the first tap/key anywhere on the page is the gesture that unlocks audio.
    if (!sharedEnabled || !sharedCtx || sharedCtx.state === 'running') return
    const unlock = () => {
      void sharedCtx?.resume().finally(sync)
    }
    // iOS Safari only counts touchend/click as an audio-unlocking gesture, not always pointerdown.
    const events = ['pointerdown', 'touchend', 'click', 'keydown'] as const
    for (const e of events) window.addEventListener(e, unlock, { capture: true })
    return () => {
      for (const e of events) window.removeEventListener(e, unlock, { capture: true })
    }
  }, [sync])

  useEffect(() => {
    // Re-bind to this mount and pick up a state change that happened while the page was away.
    if (sharedCtx) sharedCtx.onstatechange = sync
    sync()
    return () => {
      if (sharedCtx?.onstatechange === sync) sharedCtx.onstatechange = null
    }
  }, [sync])

  return { state, enable, disable, play }
}
