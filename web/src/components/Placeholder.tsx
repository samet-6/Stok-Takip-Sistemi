import { Alert } from 'react-bootstrap'

/** Temporary stand-in for a page that is not implemented yet. */
export function Placeholder({ title }: { title: string }) {
  return (
    <>
      <h2 className="mb-4">{title}</h2>
      <Alert variant="secondary">Bu sayfa yakında eklenecek.</Alert>
    </>
  )
}
