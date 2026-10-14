import type { ComponentProps } from 'react'
import { SlotNotice } from '@/components/slots/ui/SlotNotice'
import { SlotVerticalProvider } from '@/components/slots/SlotVerticalContext'
import { staysVertical } from '../vertical'
import type { StayTextKey } from '../utils/stayTexts'

type Props = Omit<ComponentProps<typeof SlotNotice>, 'textKey'> & { textKey: StayTextKey }

/** One legal microcopy of «Дома» — the shared `SlotNotice` in the «Дома» vertical, with the `Stay…` key of the text. */
export function StayNotice(props: Props) {
  return (
    <SlotVerticalProvider value={staysVertical}>
      <SlotNotice {...props} />
    </SlotVerticalProvider>
  )
}
