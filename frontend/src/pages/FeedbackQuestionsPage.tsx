import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiErrorMessage, mastersApi } from '../lib/api'
import type {
  FeedbackQuestionDto,
  FeedbackQuestionUpsertRequest,
  VisitFeedbackDto,
} from '../types/api'
import { Alert, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel } from '../components/ui/Field'
import { formatDateTime, cn } from '../lib/utils'

const blank = (): FeedbackQuestionUpsertRequest => ({
  prompt: '',
  isRequired: true,
  isActive: true,
  sortOrder: 0,
})

function Stars({ rating }: { rating: number }) {
  return (
    <span className="inline-flex gap-0.5 text-amber-500" aria-label={`${rating} of 5 stars`}>
      {[1, 2, 3, 4, 5].map((n) => (
        <span key={n} className={cn('text-sm', n <= rating ? 'opacity-100' : 'opacity-25')}>
          ★
        </span>
      ))}
    </span>
  )
}

export function FeedbackQuestionsPage() {
  const [tab, setTab] = useState<'questions' | 'responses'>('responses')
  const [items, setItems] = useState<FeedbackQuestionDto[]>([])
  const [responses, setResponses] = useState<VisitFeedbackDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [editing, setEditing] = useState<FeedbackQuestionDto | null>(null)
  const [form, setForm] = useState<FeedbackQuestionUpsertRequest>(blank())
  const [saving, setSaving] = useState(false)

  const loadQuestions = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setItems(await mastersApi.feedbackQuestions(false))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [])

  const loadResponses = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setResponses(await mastersApi.feedbackResponses(100))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    if (tab === 'questions') void loadQuestions()
    else void loadResponses()
  }, [tab, loadQuestions, loadResponses])

  function startCreate() {
    setEditing(null)
    setForm({ ...blank(), sortOrder: items.length + 1 })
  }

  function startEdit(item: FeedbackQuestionDto) {
    setEditing(item)
    setForm({
      prompt: item.prompt,
      isRequired: item.isRequired,
      isActive: item.isActive,
      sortOrder: item.sortOrder,
    })
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setMessage(null)
    setError(null)
    try {
      if (editing) await mastersApi.updateFeedbackQuestion(editing.id, form)
      else await mastersApi.createFeedbackQuestion(form)
      setMessage('Saved.')
      setEditing(null)
      setForm(blank())
      await loadQuestions()
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function deactivate(id: string) {
    setError(null)
    try {
      await mastersApi.deactivateFeedbackQuestion(id)
      setMessage('Deactivated.')
      await loadQuestions()
    } catch (e) {
      setError(apiErrorMessage(e))
    }
  }

  return (
    <div>
      <PageHeader
        title="Visitor feedback"
        subtitle="View submitted star ratings, or manage the questions shown at check-out."
        actions={
          tab === 'questions' ? (
            <Button variant="amber" onClick={startCreate}>
              Add question
            </Button>
          ) : (
            <Button variant="secondary" onClick={() => void loadResponses()}>
              Refresh
            </Button>
          )
        }
      />

      <div className="mb-4 flex gap-2">
        <Button
          variant={tab === 'responses' ? 'primary' : 'secondary'}
          onClick={() => setTab('responses')}
        >
          Responses
        </Button>
        <Button
          variant={tab === 'questions' ? 'primary' : 'secondary'}
          onClick={() => setTab('questions')}
        >
          Questions
        </Button>
      </div>

      {error ? (
        <div className="mb-4">
          <Alert tone="error">{error}</Alert>
        </div>
      ) : null}
      {message ? (
        <div className="mb-4">
          <Alert tone="success">{message}</Alert>
        </div>
      ) : null}

      {tab === 'responses' ? (
        <Panel className="overflow-x-auto p-0">
          {loading ? <Spinner /> : null}
          {!loading && responses.length === 0 ? (
            <div className="p-4">
              <EmptyState
                title="No feedback yet"
                description="Ratings appear here after visitors complete check-out with the feedback form."
              />
            </div>
          ) : null}
          {!loading && responses.length > 0 ? (
            <ul className="divide-y divide-border/70">
              {responses.map((r) => (
                <li key={r.id} className="px-4 py-4">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <div className="font-semibold text-ink">{r.visitorName}</div>
                      <div className="text-sm text-ink-muted">
                        {r.companyName}
                        {r.email ? ` · ${r.email}` : ''}
                        {r.phone ? ` · ${r.phone}` : ''}
                      </div>
                      <div className="mt-0.5 text-xs text-ink-muted">
                        {r.visitNumber} · {formatDateTime(r.submittedAt)}
                      </div>
                    </div>
                    <Link
                      to={`/visitors/${r.visitId}`}
                      className="text-sm font-medium text-primary underline-offset-2 hover:underline"
                    >
                      View visit
                    </Link>
                  </div>
                  <ul className="mt-3 space-y-1.5">
                    {r.answers.map((a, i) => (
                      <li key={`${r.id}-${i}`} className="flex flex-wrap items-center justify-between gap-2 text-sm">
                        <span className="text-ink">{a.questionText}</span>
                        <Stars rating={a.rating} />
                      </li>
                    ))}
                  </ul>
                  {r.comments ? (
                    <p className="mt-2 rounded-lg bg-mint/50 px-3 py-2 text-sm text-ink">{r.comments}</p>
                  ) : null}
                </li>
              ))}
            </ul>
          ) : null}
        </Panel>
      ) : (
        <div className="grid gap-4 xl:grid-cols-[1fr_360px]">
          <Panel className="overflow-x-auto p-0">
            {loading ? <Spinner /> : null}
            {!loading && items.length === 0 ? (
              <div className="p-4">
                <EmptyState title="No feedback questions" description="Add questions to collect star ratings at check-out." />
              </div>
            ) : null}
            {!loading && items.length > 0 ? (
              <table className="min-w-full text-left text-sm">
                <thead className="bg-gray-50 text-gray-500">
                  <tr>
                    <th className="px-4 py-3">#</th>
                    <th className="px-4 py-3">Question</th>
                    <th className="px-4 py-3">Required</th>
                    <th className="px-4 py-3">Active</th>
                    <th className="px-4 py-3" />
                  </tr>
                </thead>
                <tbody>
                  {items.map((item) => (
                    <tr key={item.id} className="border-t border-gray-100">
                      <td className="px-4 py-3">{item.sortOrder}</td>
                      <td className="px-4 py-3 font-medium">{item.prompt}</td>
                      <td className="px-4 py-3">{item.isRequired ? 'Yes' : 'No'}</td>
                      <td className="px-4 py-3">{item.isActive ? 'Yes' : 'No'}</td>
                      <td className="px-4 py-3 text-right">
                        <div className="flex justify-end gap-2">
                          <Button size="sm" variant="secondary" onClick={() => startEdit(item)}>
                            Edit
                          </Button>
                          {item.isActive ? (
                            <Button size="sm" variant="danger" onClick={() => void deactivate(item.id)}>
                              Deactivate
                            </Button>
                          ) : null}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : null}
          </Panel>

          <Panel>
            <h2 className="mb-3 text-sm font-semibold text-steel">{editing ? 'Edit question' : 'New question'}</h2>
            <form onSubmit={save} className="grid gap-3">
              <div>
                <FieldLabel htmlFor="fb-prompt">Question text</FieldLabel>
                <TextInput
                  id="fb-prompt"
                  required
                  maxLength={300}
                  value={form.prompt}
                  onChange={(e) => setForm({ ...form, prompt: e.target.value })}
                  placeholder="e.g. Overall visit experience"
                />
              </div>
              <div>
                <FieldLabel htmlFor="fb-sort">Sort order</FieldLabel>
                <TextInput
                  id="fb-sort"
                  type="number"
                  value={form.sortOrder}
                  onChange={(e) => setForm({ ...form, sortOrder: Number(e.target.value) })}
                />
              </div>
              <label className="flex min-h-11 items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={form.isRequired}
                  onChange={(e) => setForm({ ...form, isRequired: e.target.checked })}
                  className="accent-primary"
                />
                Required (must rate before check-out)
              </label>
              <label className="flex min-h-11 items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={form.isActive}
                  onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                  className="accent-primary"
                />
                Active
              </label>
              <Button type="submit" disabled={saving}>
                {saving ? 'Saving…' : 'Save'}
              </Button>
            </form>
          </Panel>
        </div>
      )}
    </div>
  )
}
