import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@/index.css'
import { GoodsApp } from './GoodsApp'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <GoodsApp />
  </StrictMode>,
)
