import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@/index.css'

// Каркас (DO-42-02, ARCHITECTURE_CYCLE42.md §42.13.2): заглушка, чтобы сборка и CI были зелёными с первого дня.
// FE-42-0 заменяет её на BaniApp.
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <main>ezbook · Бани</main>
  </StrictMode>,
)
