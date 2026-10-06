import { useState, type FormEvent, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { BrandLogo } from '../components/BrandLogo'
import { Button } from '../components/ui/Button'
import { FieldLabel, PasswordInput, TextInput } from '../components/ui/Field'
import { Alert } from '../components/ui/Panel'
import { apiErrorMessage, authApi } from '../lib/api'
import { LanguageSwitcher, useI18n } from '../i18n'

function AuthCard({ title, subtitle, children }: { title: string; subtitle: string; children: ReactNode }) {
  return (
    <div className="relative flex min-h-screen items-center justify-center bg-page px-4 py-10 sm:px-8">
      <div className="absolute inset-0 bg-brand-soft opacity-70" />
      <div className="absolute right-4 top-4 z-10">
        <LanguageSwitcher />
      </div>
      <div className="relative w-full max-w-md rounded-3xl border border-border/80 bg-white p-6 shadow-elevated sm:p-8">
        <BrandLogo className="h-12" />
        <h1 className="mt-5 text-2xl font-semibold text-ink">{title}</h1>
        <p className="mt-1 text-sm text-ink-muted">{subtitle}</p>
        <div className="mt-6">{children}</div>
      </div>
    </div>
  )
}

export function ForgotPasswordPage() {
  const { t } = useI18n()
  const [value, setValue] = useState('')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    setMessage(null)
    try {
      setMessage((await authApi.forgotPassword(value.trim())) ?? null)
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthCard title={t('login.forgotTitle')} subtitle={t('login.forgotHelp')}>
      <form onSubmit={onSubmit} className="space-y-4">
        <div>
          <FieldLabel htmlFor="resetLogin">{t('login.username')}</FieldLabel>
          <TextInput
            id="resetLogin"
            autoComplete="username"
            autoCapitalize="none"
            autoCorrect="off"
            spellCheck={false}
            value={value}
            onChange={(e) => setValue(e.target.value)}
            required
          />
        </div>
        {message ? <Alert tone="success">{message}</Alert> : null}
        {error ? <Alert tone="error">{error}</Alert> : null}
        <Button type="submit" size="lg" className="w-full" loading={busy}>
          {busy ? t('login.sending') : t('login.sendLink')}
        </Button>
        <Link to="/login" className="block text-center text-sm font-medium text-primary hover:underline">
          {t('login.backToSignIn')}
        </Link>
      </form>
    </AuthCard>
  )
}

export function ResetPasswordPage() {
  const { t } = useI18n()
  const [params] = useSearchParams()
  const userId = params.get('uid') ?? ''
  const token = params.get('token') ?? ''
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    if (password !== confirm) {
      setError(t('login.passwordsDoNotMatch'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      setDone((await authApi.resetPassword(userId, token, password)) ?? 'Password changed.')
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthCard title={t('login.resetTitle')} subtitle={t('login.resetHelp')}>
      {!userId || !token ? (
        <div className="space-y-4">
          <Alert tone="error">{t('login.resetInvalidLink')}</Alert>
          <Link to="/forgot-password" className="block text-center text-sm font-medium text-primary hover:underline">
            {t('login.forgotPassword')}
          </Link>
        </div>
      ) : done ? (
        <div className="space-y-4">
          <Alert tone="success">{done}</Alert>
          <Link to="/login">
            <Button size="lg" className="w-full">{t('login.backToSignIn')}</Button>
          </Link>
        </div>
      ) : (
        <form onSubmit={onSubmit} className="space-y-4">
          <div>
            <FieldLabel htmlFor="newPassword">{t('login.newPassword')}</FieldLabel>
            <PasswordInput
              id="newPassword"
              autoComplete="new-password"
              minLength={10}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </div>
          <div>
            <FieldLabel htmlFor="confirmPassword">{t('login.confirmPassword')}</FieldLabel>
            <PasswordInput
              id="confirmPassword"
              autoComplete="new-password"
              minLength={10}
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              required
            />
          </div>
          {error ? <Alert tone="error">{error}</Alert> : null}
          <Button type="submit" size="lg" className="w-full" loading={busy}>
            {t('login.resetButton')}
          </Button>
          <Link to="/login" className="block text-center text-sm font-medium text-primary hover:underline">
            {t('login.backToSignIn')}
          </Link>
        </form>
      )}
    </AuthCard>
  )
}
