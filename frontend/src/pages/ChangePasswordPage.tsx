import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '../components/ui/Button'
import { FieldLabel, PasswordInput } from '../components/ui/Field'
import { Alert, PageHeader, Panel } from '../components/ui/Panel'
import { apiErrorMessage } from '../lib/api'
import { homePathForRoles } from '../lib/utils'
import { useAuthStore } from '../store/authStore'

export function ChangePasswordPage() {
  const navigate = useNavigate()
  const user = useAuthStore((s) => s.user)
  const changePassword = useAuthStore((s) => s.changePassword)
  const loading = useAuthStore((s) => s.loading)

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)

    if (newPassword.length < 10) {
      setError('New password must be at least 10 characters and include upper, lower, digit, and a symbol.')
      return
    }
    if (newPassword !== confirm) {
      setError('New password and confirmation do not match.')
      return
    }

    setSubmitting(true)
    try {
      await changePassword({ currentPassword, newPassword })
      navigate(homePathForRoles(user?.roles ?? [], user?.allowedMenuKeys), { replace: true })
    } catch (err) {
      setError(apiErrorMessage(err, 'Unable to change password.'))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="mx-auto max-w-lg">
      <PageHeader
        title="Change password"
        subtitle={
          user?.mustChangePassword
            ? 'You must set a new password before continuing.'
            : 'Update your account password'
        }
      />

      <Panel>
        <form onSubmit={onSubmit} className="space-y-4">
          <div>
            <FieldLabel htmlFor="currentPassword">Current password</FieldLabel>
            <PasswordInput
              id="currentPassword"
              autoComplete="current-password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              required
            />
          </div>
          <div>
            <FieldLabel htmlFor="newPassword">New password</FieldLabel>
            <PasswordInput
              id="newPassword"
              autoComplete="new-password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              required
              minLength={8}
            />
          </div>
          <div>
            <FieldLabel htmlFor="confirmPassword">Confirm new password</FieldLabel>
            <PasswordInput
              id="confirmPassword"
              autoComplete="new-password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              required
              minLength={8}
            />
          </div>

          {error ? <Alert tone="error">{error}</Alert> : null}

          <Button type="submit" variant="primary" className="w-full" loading={submitting || loading}>
            {submitting ? 'Saving…' : 'Update password'}
          </Button>
        </form>
      </Panel>
    </div>
  )
}
