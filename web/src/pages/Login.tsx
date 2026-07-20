import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Link, useNavigate } from 'react-router'
import { Alert, Button, Card, Form, Spinner } from 'react-bootstrap'
import { login as loginRequest } from '../api/auth'
import { useAuthStore } from '../stores/authStore'
import { useToast } from '../components/ToastProvider'
import { parseProblemDetails, problemMessage } from '../lib/problemDetails'
import { applyServerFieldErrors } from '../lib/formErrors'

const schema = z.object({
  email: z
    .string()
    .min(1, 'Bu alan zorunludur')
    .email('Geçerli bir e-posta girin'),
  password: z.string().min(1, 'Bu alan zorunludur'),
})

type LoginForm = z.infer<typeof schema>

export default function Login() {
  const [formError, setFormError] = useState<string | null>(null)
  const navigate = useNavigate()
  const loginToStore = useAuthStore((s) => s.login)
  const { showError } = useToast()

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<LoginForm>({ resolver: zodResolver(schema) })

  const onSubmit = async (values: LoginForm) => {
    setFormError(null)
    try {
      const auth = await loginRequest(values)
      loginToStore(auth)
      navigate('/')
    } catch (err) {
      const problem = parseProblemDetails(err)
      if (problem.status === 401) {
        setFormError('E-posta veya şifre hatalı.')
      } else if (problem.status === 400 && applyServerFieldErrors(problem, setError, ['email', 'password'])) {
        // field errors surfaced inline
      } else {
        showError(problemMessage(problem))
      }
    }
  }

  return (
    <div className="d-flex justify-content-center">
      <Card style={{ width: '100%', maxWidth: 420 }} className="mt-4 shadow-sm">
        <Card.Body>
          <Card.Title as="h2" className="mb-4 text-center">
            Giriş Yap
          </Card.Title>
          {formError && <Alert variant="danger">{formError}</Alert>}
          <Form onSubmit={handleSubmit(onSubmit)} noValidate>
            <Form.Group className="mb-3" controlId="login-email">
              <Form.Label>E-posta</Form.Label>
              <Form.Control
                type="email"
                {...register('email')}
                isInvalid={!!errors.email}
                autoComplete="email"
              />
              <Form.Control.Feedback type="invalid">
                {errors.email?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Form.Group className="mb-4" controlId="login-password">
              <Form.Label>Şifre</Form.Label>
              <Form.Control
                type="password"
                {...register('password')}
                isInvalid={!!errors.password}
                autoComplete="current-password"
              />
              <Form.Control.Feedback type="invalid">
                {errors.password?.message}
              </Form.Control.Feedback>
            </Form.Group>
            <Button type="submit" className="w-100" disabled={isSubmitting}>
              {isSubmitting ? (
                <>
                  <Spinner as="span" size="sm" animation="border" className="me-2" />
                  Giriş yapılıyor…
                </>
              ) : (
                'Giriş Yap'
              )}
            </Button>
          </Form>
          <div className="text-center mt-3">
            Hesabınız yok mu? <Link to="/register">Kayıt olun</Link>
          </div>
        </Card.Body>
      </Card>
    </div>
  )
}
