import { Alert } from 'react-bootstrap'

/** Temporary stand-in for pages implemented in later phases (F4b–F4d). */
export function Placeholder({ title }: { title: string }) {
  return (
    <>
      <h2 className="mb-4">{title}</h2>
      <Alert variant="secondary">Bu sayfa yakında eklenecek.</Alert>
    </>
  )
}
