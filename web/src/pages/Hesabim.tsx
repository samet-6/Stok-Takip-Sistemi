import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
  Alert,
  Button,
  Card,
  Col,
  Form,
  Modal,
  Pagination,
  Row,
  Spinner,
} from 'react-bootstrap'
import { changePassword } from '../api/account'
import { getStockMovements } from '../api/stockMovements'
import { useAuthStore } from '../stores/authStore'
import { useToast } from '../components/toastContext'
import { MovementsTable } from '../components/MovementsTable'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'
import { applyServerFieldErrors } from '../lib/formErrors'

// New-password policy mirror (backend is the source of truth; this is for instant UX).
const newPasswordRules = z
  .string()
  .min(8, 'Şifre en az 8 karakter olmalı')
  .regex(/[a-z]/, 'En az bir küçük harf içermeli')
  .regex(/[A-Z]/, 'En az bir büyük harf içermeli')
  .regex(/\d/, 'En az bir rakam içermeli')
  .regex(/[^A-Za-z0-9]/, 'En az bir özel karakter içermeli')

const passwordSchema = z
  .object({
    currentPassword: z.string().min(1, 'Bu alan zorunludur'),
    newPassword: newPasswordRules,
    confirmPassword: z.string().min(1, 'Bu alan zorunludur'),
  })
  .refine((v) => v.newPassword !== v.currentPassword, {
    path: ['newPassword'],
    message: 'Yeni şifre mevcut şifreden farklı olmalı',
  })
  .refine((v) => v.newPassword === v.confirmPassword, {
    path: ['confirmPassword'],
    message: 'Şifreler eşleşmiyor',
  })

type PasswordForm = z.infer<typeof passwordSchema>

const EMPTY: PasswordForm = { currentPassword: '', newPassword: '', confirmPassword: '' }

export default function Hesabim() {
  const { showSuccess, showError } = useToast()
  const user = useAuthStore((s) => s.user)
  const applyAuth = useAuthStore((s) => s.login)

  const [showPwModal, setShowPwModal] = useState(false)
  const [page, setPage] = useState(1)

  // Own movements only. The server forces a Çalışan to their own id; passing the
  // caller's own id also constrains an Admin to their own here (spec).
  const movementsQuery = useQuery({
    queryKey: ['my-movements', user?.id, page],
    queryFn: () => getStockMovements({ userId: user!.id, page, pageSize: 10 }),
    enabled: !!user,
  })

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<PasswordForm>({ resolver: zodResolver(passwordSchema), defaultValues: EMPTY })

  const openPwModal = () => {
    reset(EMPTY)
    setShowPwModal(true)
  }

  const pwMutation = useMutation({
    mutationFn: (values: PasswordForm) =>
      changePassword({
        currentPassword: values.currentPassword,
        newPassword: values.newPassword,
      }),
    onSuccess: (res) => {
      // Adopt the fresh JWT so the SecurityStamp bump doesn't drop the session (ADR-0001).
      if (user) applyAuth({ token: res.token, expiresAt: res.expiresAt, user })
      showSuccess('Şifreniz güncellendi')
      setShowPwModal(false)
    },
    onError: (err) => {
      const problem = parseProblemDetails(err)
      if (
        problem.status === 400 &&
        applyServerFieldErrors(problem, setError, ['currentPassword', 'newPassword'])
      ) {
        // field errors surfaced inline
      } else {
        showError(problemMessage(problem))
      }
    },
  })

  if (!user) return null

  const data = movementsQuery.data

  return (
    <>
      <h2 className="mb-4">Hesabım</h2>

      <Card className="mb-4">
        <Card.Body>
          <div className="d-flex justify-content-between align-items-start">
            <Row className="flex-grow-1 g-0">
              <Col md={6} className="mb-3">
                <div className="text-muted small">Ad Soyad</div>
                <div>{user.fullName}</div>
              </Col>
              <Col md={6} className="mb-3">
                <div className="text-muted small">E-posta</div>
                <div>{user.email}</div>
              </Col>
            </Row>
            <Button variant="outline-secondary" onClick={openPwModal}>
              Şifre Değiştir
            </Button>
          </div>
        </Card.Body>
      </Card>

      <h3 className="h5 mb-3">Stok Hareketlerim</h3>
      {movementsQuery.isLoading ? (
        <div className="text-center py-5">
          <Spinner animation="border" />
        </div>
      ) : movementsQuery.isError ? (
        <Alert variant="danger">Hareketler yüklenemedi.</Alert>
      ) : !data || data.items.length === 0 ? (
        <Alert variant="secondary">Henüz hareketiniz yok.</Alert>
      ) : (
        <>
          <MovementsTable items={data.items} />

          {data.totalPages > 1 && (
            <Pagination className="justify-content-center">
              <Pagination.Prev disabled={page <= 1} onClick={() => setPage(page - 1)} />
              {Array.from({ length: data.totalPages }, (_, i) => i + 1).map((n) => (
                <Pagination.Item key={n} active={n === page} onClick={() => setPage(n)}>
                  {n}
                </Pagination.Item>
              ))}
              <Pagination.Next
                disabled={page >= data.totalPages}
                onClick={() => setPage(page + 1)}
              />
            </Pagination>
          )}
        </>
      )}

      {/* Change password modal (bank-style: current password required) */}
      <Modal show={showPwModal} onHide={() => setShowPwModal(false)} centered>
        <Form onSubmit={handleSubmit((v) => pwMutation.mutate(v))} noValidate>
          <Modal.Header closeButton>
            <Modal.Title>Şifre Değiştir</Modal.Title>
          </Modal.Header>
          <Modal.Body>
            <Form.Group className="mb-3" controlId="pw-current">
              <Form.Label>Mevcut Şifre</Form.Label>
              <Form.Control
                type="password"
                {...register('currentPassword')}
                isInvalid={!!errors.currentPassword}
                autoComplete="current-password"
                autoFocus
              />
              <Form.Control.Feedback type="invalid">
                {errors.currentPassword?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Form.Group className="mb-3" controlId="pw-new">
              <Form.Label>Yeni Şifre</Form.Label>
              <Form.Control
                type="password"
                {...register('newPassword')}
                isInvalid={!!errors.newPassword}
                autoComplete="new-password"
              />
              <Form.Control.Feedback type="invalid">
                {errors.newPassword?.message}
              </Form.Control.Feedback>
              <Form.Text muted>
                En az 8 karakter; büyük/küçük harf, rakam ve özel karakter içermeli.
              </Form.Text>
            </Form.Group>
            <Form.Group className="mb-1" controlId="pw-confirm">
              <Form.Label>Yeni Şifre (Tekrar)</Form.Label>
              <Form.Control
                type="password"
                {...register('confirmPassword')}
                isInvalid={!!errors.confirmPassword}
                autoComplete="new-password"
              />
              <Form.Control.Feedback type="invalid">
                {errors.confirmPassword?.message}
              </Form.Control.Feedback>
            </Form.Group>
          </Modal.Body>
          <Modal.Footer>
            <Button
              variant="secondary"
              onClick={() => setShowPwModal(false)}
              disabled={pwMutation.isPending}
            >
              Vazgeç
            </Button>
            <Button type="submit" variant="primary" disabled={pwMutation.isPending}>
              {pwMutation.isPending ? (
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
    </>
  )
}
