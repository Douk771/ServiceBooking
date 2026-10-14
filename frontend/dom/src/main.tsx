import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@/index.css'
import { SlotVerticalProvider } from '@/components/slots/SlotVerticalContext'
import { DomApp } from './DomApp'
import { staysVertical } from './vertical'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <SlotVerticalProvider value={staysVertical}>
      <DomApp />
    </SlotVerticalProvider>
  </StrictMode>,
)
