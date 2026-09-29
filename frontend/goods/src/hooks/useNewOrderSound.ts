import { useCallback, useEffect, useRef, useState } from 'react'

export type SoundState = 'off' | 'on' | 'blocked' | 'unsupported'

type AudioCtor = typeof AudioContext

function audioContextCtor(): AudioCtor | undefined {
  const w = window as unknown as { AudioContext?: AudioCtor; webkitAudioContext?: AudioCtor }
  return w.AudioContext ?? w.webkitAudioContext
}

/**
 * «Включить звук» (§397.3). Browsers refuse audio before a user gesture, so `enable()` must be called from a
 * click: it creates/resumes the AudioContext and plays a short test signal. The signal is generated (two tones,
 * ~0.6 s) — no audio files. The state is shown permanently: off, on, or blocked by the browser
 * (`AudioContext.state !== 'running'` after it was enabled).
 */
export function useNewOrderSound() {
  const ctxRef = useRef<AudioContext | null>(null)
  const enabled = useRef(false)
  const [state, setState] = useState<SoundState>(() => (audioContextCtor() ? 'off' : 'unsupported'))

  const sync = useCallback(() => {
    const ctx = ctxRef.current
    if (!enabled.current || !ctx) return
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
    const ctx = ctxRef.current
    if (!enabled.current || !ctx) return
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
    if (!ctxRef.current) {
      ctxRef.current = new Ctor()
      ctxRef.current.onstatechange = sync
    }
    enabled.current = true
    try {
      await ctxRef.current.resume()
    } catch {
      // stays blocked — reflected below
    }
    sync()
    play()
  }, [play, sync])

  const disable = useCallback(() => {
    enabled.current = false
    setState('off')
  }, [])

  useEffect(
    () => () => {
      enabled.current = false
      void ctxRef.current?.close().catch(() => {})
      ctxRef.current = null
    },
    [],
  )

  return { state, enable, disable, play }
}
