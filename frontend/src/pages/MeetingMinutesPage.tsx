import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { apiErrorMessage, mastersApi, visitorsApi } from '../lib/api'
import type { EmployeeDto, VisitorDetailDto } from '../types/api'
import { useAuthStore } from '../store/authStore'
import { EmployeeNameInput } from '../components/EmployeeNameInput'
import { Alert, Badge, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { FieldLabel, TextArea, TextInput } from '../components/ui/Field'
import { formatDateTime } from '../lib/utils'

const MAX_TEXT = 4000
const MAX_ACTION_ITEMS = 20

interface ActionRow {
  key: number
  task: string
  owner: string
  dueDate: string
}

let nextRowKey = 1
function newRow(task = '', owner = '', dueDate = ''): ActionRow {
  return { key: nextRowKey++, task, owner, dueDate }
}

/** Minutes of meeting on one screen: visit summary, discussion, decisions, action items and next meeting. */
export function MeetingMinutesPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [visitor, setVisitor] = useState<VisitorDetailDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [discussion, setDiscussion] = useState('')
  const [decisions, setDecisions] = useState('')
  const [actions, setActions] = useState<ActionRow[]>([])
  const [nextMeeting, setNextMeeting] = useState('')
  const [employees, setEmployees] = useState<EmployeeDto[]>([])
  const userLocationId = useAuthStore((s) => s.user?.locationId ?? null)
  useEffect(() => {
    let cancelled = false
    mastersApi
      .hosts(undefined, true, userLocationId || undefined)
      .then((list) => {
        if (!cancelled) setEmployees(list)
      })
      .catch(() => {
        // Owner stays a plain text field when the employee list is unavailable.
      })
    return () => {
      cancelled = true
    }
  }, [userLocationId])

  function fillForm(v: VisitorDetailDto) {
    setVisitor(v)
    setDiscussion(v.meetingMinutes || '')
    setDecisions(v.momDecisions || '')
    setActions((v.momActionItems ?? []).map((a) => newRow(a.task, a.owner || '', a.dueDate || '')))
    setNextMeeting(v.momNextMeetingDate || '')
  }

  useEffect(() => {
    if (!id) return
    let cancelled = false
    void (async () => {
      setLoading(true)
      setError(null)
      try {
        const v = await visitorsApi.get(id)
        if (cancelled) return
        if (v.momEmailSentAt) {
          navigate(`/visitors/${id}`, { replace: true })
          return
        }
        fillForm(v)
      } catch (e) {
        if (!cancelled) setError(apiErrorMessage(e, 'Unable to load this visit.'))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [id])

  function goBack() {
    const idx = (window.history.state as { idx?: number } | null)?.idx ?? 0
    if (idx > 0) navigate(-1)
    else navigate(`/visitors/${id}`, { replace: true })
  }

  function patchAction(key: number, patch: Partial<ActionRow>) {
    setActions((rows) => rows.map((r) => (r.key === key ? { ...r, ...patch } : r)))
  }

  async function save(sendEmail: boolean) {
    if (!id || !visitor) return
    if (!discussion.trim()) {
      setError('Enter the discussion points.')
      return
    }
    const filled = actions.filter((a) => a.task.trim() || a.owner.trim() || a.dueDate)
    if (filled.some((a) => !a.task.trim())) {
      setError('Each action item needs a task. Remove rows you do not need.')
      return
    }
    if (sendEmail && !window.confirm('Send the MOM email now? After sending, these minutes become read-only and cannot be changed.')) return
    setSaving(true)
    setError(null)
    try {
      await visitorsApi.saveMeetingMinutes(id, {
        meetingMinutes: discussion.trim(),
        decisions: decisions.trim() || null,
        actionItems: filled.map((a) => ({
          task: a.task.trim(),
          owner: a.owner.trim() || null,
          dueDate: a.dueDate || null,
        })),
        nextMeetingDate: nextMeeting || null,
        sendEmail,
      })
      goBack()
    } catch (e) {
      setError(apiErrorMessage(e, 'Unable to save the minutes of meeting.'))
      setSaving(false)
    }
  }

  async function markNotRequired() {
    if (!id || !visitor) return
    if (!window.confirm(`Mark minutes of meeting as not required for ${visitor.visitorName}? No MOM email will be sent.`)) return
    setSaving(true)
    setError(null)
    try {
      await visitorsApi.setMomNotRequired(id, true)
      goBack()
    } catch (e) {
      setError(apiErrorMessage(e))
      setSaving(false)
    }
  }

  if (loading) return <Spinner label="Loading visit…" />
  if (!visitor) return error ? <Alert tone="error" persistent>{error}</Alert> : null

  const status = String(visitor.statusLabel).toLowerCase()
  const canRecord = status.includes('inside') || status.includes('checkedout')

  return (
    <div>
      {saving ? <LoadingMask label="Saving minutes…" /> : null}
      <PageHeader
        eyebrow="Minutes of meeting"
        title={visitor.visitorName}
        subtitle={[visitor.visitNumber, visitor.companyName].filter(Boolean).join(' · ')}
        actions={
          <Link to={`/visitors/${visitor.visitId}`}>
            <Button variant="secondary">Back to visit</Button>
          </Link>
        }
      />

      {error ? <Alert tone="error">{error}</Alert> : null}

      <Panel className="mb-4">
        <dl className="grid gap-3 text-sm sm:grid-cols-3 xl:grid-cols-6">
          <Summary label="Person to meet" value={visitor.hostName} />
          <Summary label="Department" value={visitor.departmentName} />
          <Summary label="Meeting hall" value={visitor.meetingHallName || '—'} />
          <Summary label="Purpose" value={visitor.purposes.join(', ') || '—'} />
          <Summary label="Check-in" value={formatDateTime(visitor.checkInAt)} />
          <Summary label="Check-out" value={visitor.checkOutAt ? formatDateTime(visitor.checkOutAt) : 'Still inside'} />
        </dl>
      </Panel>

      {!canRecord ? (
        <Alert tone="info" persistent>Minutes of meeting can be recorded once the visitor has checked in.</Alert>
      ) : (
        <Panel>
          {visitor.momNotRequired && !visitor.hasMeetingMinutes ? (
            <div className="mb-4">
              <Alert tone="info" persistent>
                Marked as not required{visitor.momNotRequiredBy ? ` by ${visitor.momNotRequiredBy}` : ''}
                {visitor.momNotRequiredAt ? ` on ${formatDateTime(visitor.momNotRequiredAt)}` : ''}. You can still
                record minutes here.
              </Alert>
            </div>
          ) : null}

          <div className="grid gap-4 lg:grid-cols-2">
            <div>
              <FieldLabel htmlFor="momDiscussion">Discussion points *</FieldLabel>
              <TextArea
                id="momDiscussion"
                rows={7}
                maxLength={MAX_TEXT}
                value={discussion}
                onChange={(e) => setDiscussion(e.target.value)}
                placeholder="What was discussed in the meeting"
              />
              <p className="mt-1 text-right text-xs text-ink-muted">{discussion.length}/{MAX_TEXT}</p>
            </div>
            <div>
              <FieldLabel htmlFor="momDecisions">Decisions</FieldLabel>
              <TextArea
                id="momDecisions"
                rows={7}
                maxLength={MAX_TEXT}
                value={decisions}
                onChange={(e) => setDecisions(e.target.value)}
                placeholder="What was agreed"
              />
              <p className="mt-1 text-right text-xs text-ink-muted">{decisions.length}/{MAX_TEXT}</p>
            </div>
          </div>

          <div className="mt-2">
            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h2 className="text-sm font-medium text-ink">Action items</h2>
              <Button
                size="sm"
                variant="ghost"
                disabled={actions.length >= MAX_ACTION_ITEMS}
                onClick={() => setActions((rows) => [...rows, newRow()])}
              >
                + Add action item
              </Button>
            </div>
            {actions.length === 0 ? (
              <p className="rounded-xl border border-dashed border-border px-3 py-3 text-sm text-ink-muted">
                No action items. Add one for each follow-up, with who owns it and when it is due.
              </p>
            ) : (
              <div className="space-y-2">
                <div className="hidden gap-2 px-1 text-xs font-medium uppercase tracking-wide text-ink-muted sm:grid sm:grid-cols-[1fr_240px_170px_44px]">
                  <span>Task</span>
                  <span>Owner</span>
                  <span>Due date</span>
                  <span />
                </div>
                {actions.map((row, index) => (
                  <div key={row.key} className="grid gap-2 sm:grid-cols-[1fr_240px_170px_44px] sm:items-center">
                    <TextInput
                      aria-label={`Action item ${index + 1} task`}
                      placeholder="Task"
                      maxLength={500}
                      value={row.task}
                      onChange={(e) => patchAction(row.key, { task: e.target.value })}
                    />
                    <EmployeeNameInput
                      employees={employees}
                      aria-label={`Action item ${index + 1} owner`}
                      placeholder="Search employee…"
                      maxLength={150}
                      value={row.owner}
                      onChange={(owner) => patchAction(row.key, { owner })}
                    />
                    <TextInput
                      type="date"
                      aria-label={`Action item ${index + 1} due date`}
                      value={row.dueDate}
                      onChange={(e) => patchAction(row.key, { dueDate: e.target.value })}
                    />
                    <button
                      type="button"
                      className="flex h-11 w-11 items-center justify-center rounded-xl text-lg text-ink-muted hover:bg-red-50 hover:text-danger"
                      aria-label={`Remove action item ${index + 1}`}
                      onClick={() => setActions((rows) => rows.filter((r) => r.key !== row.key))}
                    >
                      ×
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div className="mt-4 grid gap-4 border-t border-border pt-4 sm:grid-cols-[220px_1fr] sm:items-end">
            <div>
              <FieldLabel htmlFor="momNextMeeting">Next meeting date</FieldLabel>
              <TextInput
                id="momNextMeeting"
                type="date"
                value={nextMeeting}
                onChange={(e) => setNextMeeting(e.target.value)}
              />
            </div>
            <div className="text-sm text-ink-muted sm:text-right">
              {visitor.momRecordedAt ? (
                <p>
                  Last saved {formatDateTime(visitor.momRecordedAt)}
                  {visitor.momRecordedBy ? ` by ${visitor.momRecordedBy}` : ''}
                </p>
              ) : null}
              <p className="mt-0.5">
                {visitor.momEmailSentAt ? (
                  <Badge className="bg-emerald-100 text-emerald-800 ring-1 ring-emerald-200">
                    MOM email sent {formatDateTime(visitor.momEmailSentAt)}
                  </Badge>
                ) : (
                  <Badge className="bg-amber-100 text-amber-900 ring-1 ring-amber-200">MOM email not sent yet</Badge>
                )}
              </p>
            </div>
          </div>

          {!visitor.email ? (
            <div className="mt-4">
              <Alert tone="warning" persistent>
                {visitor.visitorName} has no email address, so the MOM email will go to {visitor.hostName} only.
              </Alert>
            </div>
          ) : null}

          <div className="mt-4 flex flex-col-reverse gap-2 sm:flex-row sm:items-center sm:justify-between">
            <div>
              {!visitor.hasMeetingMinutes && !visitor.momNotRequired ? (
                <Button variant="secondary" disabled={saving} onClick={() => void markNotRequired()}>
                  MoM not required
                </Button>
              ) : null}
            </div>
            <div className="flex flex-col gap-2 sm:flex-row">
              <Button variant="secondary" disabled={saving} onClick={() => navigate(`/visitors/${visitor.visitId}`)}>
                Cancel
              </Button>
              <Button variant="secondary" disabled={saving || !discussion.trim()} onClick={() => void save(false)}>
                Save
              </Button>
              <Button disabled={saving || !discussion.trim()} onClick={() => void save(true)}>
                Save & send MOM email
              </Button>
            </div>
          </div>
          <p className="mt-2 text-xs text-ink-muted sm:text-right">
            The MOM email goes to {visitor.email ? `${visitor.visitorName} (${visitor.email}) and ` : ''}
            {visitor.hostName}, separately from the check-out email. Once sent, the minutes cannot be changed.
          </p>
        </Panel>
      )}
    </div>
  )
}

function Summary({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd className="font-medium text-ink">{value}</dd>
    </div>
  )
}