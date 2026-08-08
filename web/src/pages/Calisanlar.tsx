import { useState } from 'react'
import { Link } from 'react-router'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Button, Form, Modal, Spinner, Table } from 'react-bootstrap'
import { getUsers, createUser, updateUser, setUserStatus } from '../api/users'
import type { UserListDto } from '../types/api'
import { useToast } from '../components/toastContext'
import { ConfirmModal } from '../components/ConfirmModal'
import { PageHeader } from '../components/PageHeader'
import { StatusChip } from '../components/StatusChip'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'
import { applyServerFieldErrors } from '../lib/formErrors'
import { formatDate } from '../lib/format'
import { makeUserSchema } from '../lib/schemas'

type UserForm = { fullName: string; email: string; password: string }

export default function Calisanlar() {
  const qc = useQueryClient()
  const { showSuccess, showError } = useToast()

  const listQuery = useQuery({ queryKey: ['users'], queryFn: getUsers })

  // Default view = active only; toggle reveals the İşten çıkarılanlar (passive) list.
  const [showPassive, setShowPassive] = useState(false)
  // Client-side name/email search — the /users endpoint returns all rows at once.
  const [search, setSearch] = useState('')
  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<UserListDto | null>(null)
  // Deactivate/reactivate confirmation target.
  const [statusTarget, setStatusTarget] = useState<{ user: UserListDto; toActive: boolean } | null>(
    null,
  )

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<UserForm>({ resolver: zodResolver(makeUserSchema(editing !== null)) })

  const openCreate = () => {
    setEditing(null)
    reset({ fullName: '', email: '', password: '' })
    setShowForm(true)
  }
  const openEdit = (u: UserListDto) => {
    setEditing(u)
    reset({ fullName: u.fullName, email: u.email, password: '' })
    setShowForm(true)
  }

  const saveMutation = useMutation({
    mutationFn: async (values: UserForm) => {
      if (editing) {
        await updateUser(editing.id, {
          fullName: values.fullName,
          email: values.email,
          password: values.password ? values.password : null,
        })
      } else {
        await createUser({
          fullName: values.fullName,
          email: values.email,
          password: values.password,
        })
      }
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['users'] })
      showSuccess(editing ? 'Çalışan güncellendi' : 'Çalışan eklendi')
      setShowForm(false)
    },
    onError: (err) => {
      const problem = parseProblemDetails(err)
      if (problem.status === 409) {
        setError('email', { type: 'server', message: problemMessage(problem) })
      } else if (
        problem.status === 400 &&
        applyServerFieldErrors(problem, setError, ['fullName', 'email', 'password'])
      ) {
        // field errors surfaced inline
      } else {
        showError(problemMessage(problem))
      }
    },
  })

  const statusMutation = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      setUserStatus(id, { isActive }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ['users'] })
      showSuccess(vars.isActive ? 'Çalışan işe geri alındı' : 'Çalışan işten çıkarıldı')
      setStatusTarget(null)
    },
    onError: (err) => {
      showError(problemMessage(parseProblemDetails(err)))
      setStatusTarget(null)
    },
  })

  const term = search.trim().toLowerCase()
  const rows = (listQuery.data ?? [])
    .filter((u) => u.isActive !== showPassive)
    .filter(
      (u) =>
        term === '' ||
        u.fullName.toLowerCase().includes(term) ||
        u.email.toLowerCase().includes(term),
    )

  return (
    <>
      <PageHeader
        title="Çalışanlar"
        subtitle="Sistemi kullanan çalışan hesapları."
        action={
          <Button variant="primary" onClick={openCreate}>
            Yeni Eleman
          </Button>
        }
      />

      <div className="d-flex flex-wrap align-items-center gap-3 mb-3">
        <Form.Control
          type="search"
          placeholder="Ad veya e-posta ara…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{ maxWidth: 320 }}
        />
        <Form.Check
          type="switch"
          id="show-passive"
          label="İşten Ayrılanlar/Çıkarılanlar"
          checked={showPassive}
          onChange={(e) => setShowPassive(e.target.checked)}
        />
      </div>

      {listQuery.isLoading ? (
        <div className="text-center py-5">
          <Spinner animation="border" />
        </div>
      ) : (
        <div className="table-card">
          <Table hover responsive className="align-middle">
            <thead>
              <tr>
                <th>Ad Soyad</th>
                <th>E-posta</th>
                <th>İşe Giriş</th>
                {showPassive && <th>İşten Çıkış</th>}
                <th className="text-end">İşlemler</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr>
                  <td colSpan={showPassive ? 5 : 4} className="text-center text-muted py-4">
                    {term
                      ? 'Aramayla eşleşen çalışan yok.'
                      : showPassive
                        ? 'İşten çıkarılan çalışan yok.'
                        : 'Çalışan yok.'}
                  </td>
                </tr>
              ) : (
                rows.map((u) => (
                  <tr key={u.id} className={showPassive ? 'row-muted' : undefined}>
                    <td>
                      {/* Active names link to that employee's movement logs; passive rows
                          are plain text — reactivate first, then drill in (spec). */}
                      {u.isActive ? (
                        <Link to={`/calisanlar/${u.id}/hareketler`}>{u.fullName}</Link>
                      ) : (
                        <span className="d-inline-flex align-items-center gap-2">
                          {u.fullName}
                          <StatusChip variant="crit" dot={false}>
                            İşten çıkarıldı
                          </StatusChip>
                        </span>
                      )}
                    </td>
                    <td className="text-muted">{u.email}</td>
                    <td>{formatDate(u.createdAt)}</td>
                    {showPassive && (
                      <td>{u.deactivatedAt ? formatDate(u.deactivatedAt) : '—'}</td>
                    )}
                    <td className="text-end text-nowrap">
                      {u.isActive ? (
                        <>
                          <Button
                            size="sm"
                            variant="outline-secondary"
                            className="me-2"
                            onClick={() => openEdit(u)}
                          >
                            Düzenle
                          </Button>
                          <Button
                            size="sm"
                            variant="outline-danger"
                            onClick={() => setStatusTarget({ user: u, toActive: false })}
                          >
                            İşten Çıkar
                          </Button>
                        </>
                      ) : (
                        <Button
                          size="sm"
                          variant="outline-success"
                          onClick={() => setStatusTarget({ user: u, toActive: true })}
                        >
                          İşe Geri Al
                        </Button>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </Table>
        </div>
      )}

      {/* Create / Edit modal */}
      <Modal show={showForm} onHide={() => setShowForm(false)} centered>
        <Form onSubmit={handleSubmit((v) => saveMutation.mutate(v))} noValidate>
          <Modal.Header closeButton>
            <Modal.Title>{editing ? 'Çalışan Düzenle' : 'Yeni Eleman'}</Modal.Title>
          </Modal.Header>
          <Modal.Body>
            <Form.Group className="mb-3" controlId="user-fullname">
              <Form.Label>Ad Soyad</Form.Label>
              <Form.Control {...register('fullName')} isInvalid={!!errors.fullName} autoFocus />
              <Form.Control.Feedback type="invalid">
                {errors.fullName?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Form.Group className="mb-3" controlId="user-email">
              <Form.Label>E-posta</Form.Label>
              <Form.Control
                type="email"
                {...register('email')}
                isInvalid={!!errors.email}
                autoComplete="off"
              />
              <Form.Control.Feedback type="invalid">
                {errors.email?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Form.Group className="mb-1" controlId="user-password">
              <Form.Label>{editing ? 'Yeni Şifre (opsiyonel)' : 'Şifre'}</Form.Label>
              <Form.Control
                type="password"
                {...register('password')}
                isInvalid={!!errors.password}
                autoComplete="new-password"
                placeholder={editing ? 'Boş bırakırsanız değişmez' : undefined}
              />
              <Form.Control.Feedback type="invalid">
                {errors.password?.message}
              </Form.Control.Feedback>
              {!editing && (
                <Form.Text muted>
                  En az 8 karakter; büyük/küçük harf, rakam ve özel karakter içermeli.
                </Form.Text>
              )}
            </Form.Group>
          </Modal.Body>
          <Modal.Footer>
            <Button
              variant="secondary"
              onClick={() => setShowForm(false)}
              disabled={saveMutation.isPending}
            >
              Vazgeç
            </Button>
            <Button type="submit" variant="primary" disabled={saveMutation.isPending}>
              {saveMutation.isPending ? (
                <>
                  <Spinner as="span" size="sm" animation="border" className="me-2" />
                  Kaydediliyor…
                </>
              ) : (
                'Kaydet'
              )}
            </Button>
          </Modal.Footer>
        </Form>
      </Modal>

      <ConfirmModal
        show={statusTarget !== null}
        title={statusTarget?.toActive ? 'İşe Geri Al' : 'İşten Çıkar'}
        body={
          statusTarget?.toActive ? (
            <>
              <strong>{statusTarget?.user.fullName}</strong> işe geri alınsın mı? Eski şifresiyle
              tekrar giriş yapabilir.
            </>
          ) : (
            <>
              <strong>{statusTarget?.user.fullName}</strong> işten çıkarılsın mı? Oturumu anında
              kapanır ve giriş yapamaz.
            </>
          )
        }
        confirmLabel={statusTarget?.toActive ? 'İşe Geri Al' : 'İşten Çıkar'}
        confirmVariant={statusTarget?.toActive ? 'success' : 'danger'}
        confirming={statusMutation.isPending}
        onConfirm={() =>
          statusTarget &&
          statusMutation.mutate({ id: statusTarget.user.id, isActive: statusTarget.toActive })
        }
        onHide={() => setStatusTarget(null)}
      />
    </>
  )
}
