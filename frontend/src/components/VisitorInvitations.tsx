import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { apiErrorMessage, invitationsApi, mastersApi } from '../lib/api'
import type { EmployeeDto, VisitorInvitationDto, VisitorInvitationStatus } from '../types/api'
import { Alert, Badge, Panel } from './ui/Panel'
import { Button } from './ui/Button'
import { Modal } from './ui/Modal'
import { FieldLabel, TextInput } from './ui/Field'
import { EmployeeSearchField } from './EmployeeSearchField'
import { formatDate, formatDateTime, validateVisitorEmail } from '../lib/utils'

const STATUS_CLASS: Record<VisitorInvitationStatus, string> = {
  Pending: 'bg-amber-100 text-amber-800',
  Completed: 'bg-green-100 text-green-800',
  Expired: 'bg-gray-100 text-gray-600',
  Withdrawn: 'bg-gray-100 text-gray-600',
}

const STATUS_LABEL: Record<VisitorInvitationStatus, string> = {
  Pending: 'Waiting for visitor',
  Completed: 'Booked',
  Expired: 'Expired',
  Withdrawn: 'Withdrawn',
}

const COLLAPSED_COUNT = 5

type Props = {
  inviteOpen: boolean
  onInviteClose: () => void
  /** A Host login invites visitors to meet themselves, so no employee picker. */
  hostOnly: boolean
  locationId?: string
  /** Called when an invitation turns into a booking, so the page can reload its appointments. */
  onBooked?: () => void
}

/** "Invite visitor" modal and the list of invitations sent from Expected Visitors. */
export function VisitorInvitations({ inviteOpen, onInviteClose, hostOnly, locationId, onBooked }: Props) {
  const [items, setItems] = useState<VisitorInvitationDto[]>([])
  const [expanded, setExpanded] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const [email, setEmail] = useState('')
  const [emailTouched, setEmailTouched] = useState(false)
  const [hostEmployeeId, setHostEmployeeId] = useState('')
  const [employees, setEmployees] = useState<EmployeeDto[]>([])
  const [sending, setSending] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const itemsRef = useRef<VisitorInvitationDto[]>([])
  const load = useCallback(async () => {
    try {
      const list = await invitationsApi.list()
      const prev = itemsRef.current
      itemsRef.current = list
      setItems(list)
      if (list.some((i) => i.status === 'Completed' && prev.find((p) => p.id === i.id)?.status === 'Pending'))
        onBooked?.()
    } catch (err) {
      setError(apiErrorMessage(err))
    }
  }, [onBooked])

  useEffect(() => {
    void load()
  }, [load])

  // Visitors fill the form in on their own time; refresh now and then so a new booking shows up.
  const hasPending = items.some((i) => i.status === 'Pending')
  useEffect(() => {
    if (!hasPending) return
    const timer = window.setInterval(() => void load(), 60_000)
    return () => window.clearInterval(timer)
  }, [hasPending, load])

  useEffect(() => {
    if (!inviteOpen) return
    setEmail('')
    setEmailTouched(false)
    setHostEmployeeId('')
    setFormError(null)
    if (hostOnly) return
    mastersApi.hosts(undefined, true, locationId || undefined)
      .then(setEmployees)
      .catch((err) => setFormError(apiErrorMessage(err)))
  }, [inviteOpen, hostOnly, locationId])

  const emailError = validateVisitorEmail(email)

  async function send(e: FormEvent) {
    e.preventDefault()
    setEmailTouched(true)
    if (emailError) {
      setFormError(emailError)
      return
    }
    if (!hostOnly && !hostEmployeeId) {
      setFormError('Select the employee the visitor will meet.')
      return
    }
    setSending(true)
    setFormError(null)
    try {
      const { message: sent } = await invitationsApi.create(email, hostOnly ? null : hostEmployeeId)
      setMessage(sent)
      onInviteClose()
      await load()
    } catch (err) {
      setFormError(apiErrorMessage(err))
    } finally {
      setSending(false)
    }
  }

  async function resend(item: VisitorInvitationDto) {
    setBusyId(item.id)
    setError(null)
    try {
      const { message: sent } = await invitationsApi.resend(item.id)
      setMessage(sent)
      await load()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setBusyId(null)
    }
  }

  async function withdraw(item: VisitorInvitationDto) {
    if (!window.confirm(`Withdraw the invitation sent to ${item.email}? The link will stop working.`)) return
    setBusyId(item.id)
    setError(null)
    try {
      setMessage((await invitationsApi.withdraw(item.id)) ?? 'Invitation withdrawn.')
      await load()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setBusyId(null)
    }
  }

  const visible = expanded ? items : items.slice(0, COLLAPSED_COUNT)

  return (
    <>
      {error ? <div className="mb-4"><Alert tone="error">{error}</Alert></div> : null}
      {message ? <div className="mb-4"><Alert tone="success">{message}</Alert></div> : null}

      {items.length > 0 ? (
        <Panel className="mb-4">
          <div className="mb-3 flex items-center justify-between gap-2">
            <h2 className="text-sm font-semibold text-steel">Visitor invitations</h2>
            <span className="text-xs text-gray-500">Last 14 days</span>
          </div>
          <ul className="divide-y divide-gray-100">
            {visible.map((item) => (
              <li key={item.id} className="flex flex-wrap items-center justify-between gap-3 py-2">
                <div className="min-w-0 flex-1 text-sm">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="truncate font-medium text-ink">{item.visitorName || item.email}</span>
                    <Badge className={STATUS_CLASS[item.status]}>{STATUS_LABEL[item.status]}</Badge>
                  </div>
                  <div className="text-xs text-gray-500">
                    {item.visitorName ? `${item.email} · ` : ''}
                    To meet {item.hostName}
                    {item.locationName ? ` · ${item.locationName}` : ''}
                    {' · '}
                    {item.status === 'Completed' && item.visitDate
                      ? `Booked for ${formatDate(item.visitDate)} ${item.visitTime?.slice(0, 5) ?? ''}`
                      : item.status === 'Pending'
                        ? `Link valid until ${formatDateTime(item.expiresAt)}`
                        : `Sent ${formatDateTime(item.lastSentAt ?? item.createdAt)}`}
                  </div>
                  {item.emailError ? (
                    <div className="mt-1 text-xs text-danger">Email not delivered yet: {item.emailError}</div>
                  ) : null}
                </div>
                {item.status === 'Pending' || item.status === 'Expired' ? (
                  <div className="flex gap-2">
                    <Button size="sm" variant="secondary" disabled={busyId === item.id} onClick={() => void resend(item)}>
                      {busyId === item.id ? 'Working…' : 'Send new link'}
                    </Button>
                    {item.status === 'Pending' ? (
                      <Button size="sm" variant="secondary" disabled={busyId === item.id} onClick={() => void withdraw(item)}>
                        Withdraw
                      </Button>
                    ) : null}
                  </div>
                ) : null}
              </li>
            ))}
          </ul>
          {items.length > COLLAPSED_COUNT ? (
            <button
              type="button"
              className="mt-2 text-sm font-medium text-primary underline"
              onClick={() => setExpanded((v) => !v)}
            >
              {expanded ? 'Show fewer' : `Show all ${items.length}`}
            </button>
          ) : null}
        </Panel>
      ) : null}

      <Modal
        open={inviteOpen}
        title="Invite visitor"
        subtitle="The visitor gets a link to fill in their own details and face photo. It works once and expires in 24 hours."
        onClose={() => {
          if (!sending) onInviteClose()
        }}
      >
        <form onSubmit={send} className="space-y-4">
          {formError ? <Alert tone="error" persistent>{formError}</Alert> : null}
          <div>
            <FieldLabel htmlFor="invite-email">Visitor email *</FieldLabel>
            <TextInput
              id="invite-email"
              type="email"
              autoComplete="off"
              autoFocus
              maxLength={150}
              disabled={sending}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              onBlur={() => setEmailTouched(true)}
            />
            {emailTouched && emailError ? <p className="mt-1 text-xs text-danger">{emailError}</p> : null}
          </div>
          {hostOnly ? (
            <p className="text-sm text-gray-600">The visitor will be booked to meet you.</p>
          ) : (
            <EmployeeSearchField
              id="invite-host"
              employees={employees}
              value={hostEmployeeId}
              disabled={sending}
              onSelect={(emp) => setHostEmployeeId(emp?.id ?? '')}
            />
          )}
          <p className="text-xs text-gray-500">
            When the visitor submits the form, the appointment appears here under Expected Visitors and the usual
            appointment emails are sent.
          </p>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" disabled={sending} onClick={onInviteClose}>
              Cancel
            </Button>
            <Button type="submit" variant="amber" disabled={sending}>
              {sending ? 'Sending…' : 'Send invitation'}
            </Button>
          </div>
        </form>
      </Modal>
    </>
  )
}
