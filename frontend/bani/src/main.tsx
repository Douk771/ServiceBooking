import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@/index.css'
import { BaniApp } from './BaniApp'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BaniApp />
  </StrictMode>,
)
