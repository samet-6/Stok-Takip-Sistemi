import { formatDate } from '../lib/format'

/**
 * The date a catalogue row was taken out of use, rendered next to its "Pasif" chip.
 *
 * Null is not "just now": rows switched off before the stamp existed carry no date, and saying
 * so out loud is the point of this component — three inline ternaries would eventually disagree
 * about what a missing date means.
 */
export function DeactivationNote({ at }: Readonly<{ at?: string | null }>) {
  return (
    <span className="text-muted small ms-2">
      {at ? `${formatDate(at)} tarihinde pasife alındı` : 'pasife alınma tarihi kayıtlı değil'}
    </span>
  )
}
