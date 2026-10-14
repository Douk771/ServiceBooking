import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@/index.css'
import { SlotVerticalProvider } from '@/components/slots/SlotVerticalContext'
import { BaniApp } from './BaniApp'
import { bathsVertical } from './vertical'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <SlotVerticalProvider value={bathsVertical}>
      <BaniApp />
    </SlotVerticalProvider>
  </StrictMode>,
)
