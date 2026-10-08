import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@/index.css'
import { DomApp } from './DomApp'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <DomApp />
  </StrictMode>,
)
