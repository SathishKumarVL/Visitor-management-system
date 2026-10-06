import { useState, type FormEvent } from 'react'
import axios from 'axios'
import { apiErrorMessage, usersApi } from '../lib/api'
import type { EmployeeDto } from '../types/api'
import { Button } from './ui/Button'
import { Alert } from './ui/Panel'
import { FieldLabel, TextInput } from './ui/Field'

function randomFrom(chars: string) {
  const buf = new Uint32Array(1)
  crypto.getRandomValues(buf)
  return chars[buf[0] % chars.length]
}

/** Meets the server password policy: 12 chars with upper, lower, digit and symbol. */
function generatePassword() {
  const upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
  const lower = 'abcdefghijkmnpqrstuvwxyz'
  const digits = '23456789'
  const symbols = '@#$%&*!?'
  const all = upper + lower + digits + symbols
  const chars = [randomFrom(upper), randomFrom(lower), randomFrom(digits), randomFrom(symbols)]
  while (chars.length < 12) chars.push(randomFrom(all))
  for (let i = chars.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1))
    ;[chars[i], chars[j]] = [chars[j], chars[i]]
  }
  return chars.join('')
}

function suggestUsername(employee: EmployeeDto) {
  const fromEmail = employee.email?.split('@')[0]
  const base = fromEmail || employee.code || employee.fullName
  return base.toLowerCase().replace(/[^a-z0-9._-]/g, '').slice(0, 50)
}

/**
 * Creates a Host login for an employee so they can see their own visitors
 * and add minutes of meeting.
 */
export function EmployeeLoginModal({
  employee,
  onClose,
  onCreated,
}: {
  employee: EmployeeDto
  onClose: () => void
  onCreated: (updated: EmployeeDto) => void | Promise<void>
}) {
  const [username, setUsername] = useState(() => suggestUsername(employee))
  const [email, setEmail] = useState(employee.email || '')
  const [password, setPassword] = useState(generatePassword)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [created, setCreated] = useState<EmployeeDto | null>(null)
  const [copied, setCopied] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const updated = await usersApi.createForEmployee(employee.id, {
        username: username.trim(),
        email: email.trim() || null,
        password,
      })
      setCreated(updated)
      await onCreated(updated)
    } catch (err) {
      const forbiddenWithoutReason =
        axios.isAxiosError(err) && err.response?.status === 403 && !(err.response.data as { message?: string } | undefined)?.message
      setError(forbiddenWithoutReason ? 'Only a SuperAdmin can create logins for employees.' : apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function copyDetails() {
    const text = `Visitor management login\nUsername: ${created?.userName ?? username}\nTemporary password: ${password}\nYou will be asked to change the password when you first sign in.`
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      role="dialog"
      aria-modal="true"
      onClick={onClose}
    >
      <div
        className="max-h-[85vh] w-full max-w-md overflow-y-auto rounded-2xl bg-white p-5 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 className="mb-1 text-lg font-semibold text-steel">Create login</h2>
        <p className="mb-4 text-sm text-ink-muted">
          {employee.fullName} will sign in as a <strong>Host</strong>, see only their own visitors and add minutes of meeting.
        </p>

        {created ? (
          <div className="space-y-3 text-sm">
            <Alert tone="success">Login created. Share these details with {employee.fullName}.</Alert>
            <div className="rounded-xl bg-gray-50 p-3 font-mono text-sm">
              <div>Username: {created.userName}</div>
              <div>Temporary password: {password}</div>
            </div>
            <p className="text-xs text-ink-muted">
              The password is shown only now. They will be asked to change it when they first sign in.
            </p>
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={() => void copyDetails()}>
                {copied ? 'Copied' : 'Copy details'}
              </Button>
              <Button onClick={onClose}>Done</Button>
            </div>
          </div>
        ) : (
          <form onSubmit={submit} className="space-y-3">
            {error ? <Alert tone="error">{error}</Alert> : null}
            <div>
              <FieldLabel>Username *</FieldLabel>
              <TextInput required maxLength={50} value={username} onChange={(e) => setUsername(e.target.value)} />
            </div>
            <div>
              <FieldLabel>Email *</FieldLabel>
              <TextInput required type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
            </div>
            <div>
              <FieldLabel>Temporary password *</FieldLabel>
              <div className="flex gap-2">
                <TextInput required minLength={10} value={password} onChange={(e) => setPassword(e.target.value)} />
                <Button type="button" variant="secondary" onClick={() => setPassword(generatePassword())}>
                  New
                </Button>
              </div>
              <p className="mt-1 text-xs text-ink-muted">
                At least 10 characters with upper and lower case, a number and a symbol.
              </p>
            </div>
            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" variant="secondary" onClick={onClose}>Cancel</Button>
              <Button type="submit" loading={saving}>Create login</Button>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}
