import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiErrorMessage, mastersApi } from '../lib/api'
import type {
  FeedbackQuestionDto,
  FeedbackQuestionUpsertRequest,
  MasterItemDto,
  VisitFeedbackDto,
} from '../types/api'
import { Alert, EmptyState, PageHeader, Panel, Spinner } from '../components/ui/Panel'
import { LoadingMask } from '../components/LoadingMask'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel, TextSelect } from '../components/ui/Field'
import { ExcelImportButtons } from '../components/ExcelImportButtons'
import { Modal } from '../components/ui/Modal'
import { Pagination } from '../components/ui/Pagination'
import { usePagination } from '../lib/usePagination'
import { useSort } from '../lib/useSort'
import { SortHeader } from '../components/ui/SortHeader'
import { formatDateTime, cn } from '../lib/utils'

const blank = (locationId: string | null = null): FeedbackQuestionUpsertRequest => ({
  prompt: '',
  isRequired: true,
  isActive: true,
  sortOrder: 0,
  locationId,
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
  const [locations, setLocations] = useState<MasterItemDto[]>([])
  const [filterLocationId, setFilterLocationId] = useState('')
  const [responses, setResponses] = useState<VisitFeedbackDto[]>([])
  const [responsesTotal, setResponsesTotal] = useState(0)
  const [responsesPage, setResponsesPage] = useState(1)
  const [responsesPageSize, setResponsesPageSize] = useState(10)
  const { sorted: sortedQuestions, sort, onSort } = useSort(items, {
    order: (q) => q.sortOrder,
    prompt: (q) => q.prompt,
    location: (q) => q.locationName,
    required: (q) => (q.isRequired ? 0 : 1),
    active: (q) => (q.isActive ? 0 : 1),
  })
  const { pageItems: questionItems, pagination: questionPagination } = usePagination(
    sortedQuestions,
    20,
    `${filterLocationId}|${sort?.key}|${sort?.dir}`,
  )
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [editing, setEditing] = useState<FeedbackQuestionDto | null>(null)
  const [form, setForm] = useState<FeedbackQuestionUpsertRequest>(blank())
  const [saving, setSaving] = useState(false)
  const [formOpen, setFormOpen] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const loadQuestions = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setItems(await mastersApi.feedbackQuestions(false, filterLocationId || undefined))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [filterLocationId])

  const loadResponses = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const result = await mastersApi.feedbackResponsesPaged(responsesPage, responsesPageSize)
      setResponses(result.items)
      setResponsesTotal(result.totalCount)
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [responsesPage, responsesPageSize])

  useEffect(() => {
    void mastersApi.locations(true).then(setLocations).catch(() => setLocations([]))
  }, [])

  useEffect(() => {
    if (tab === 'questions') void loadQuestions()
    else void loadResponses()
  }, [tab, loadQuestions, loadResponses])

  function closeForm() {
    setFormOpen(false)
    setEditing(null)
    setFormError(null)
  }

  function startCreate() {
    setEditing(null)
    setFormError(null)
    setMessage(null)
    setForm({ ...blank(filterLocationId || null), sortOrder: items.length + 1 })
    setFormOpen(true)
  }

  function startEdit(item: FeedbackQuestionDto) {
    setEditing(item)
    setFormError(null)
    setMessage(null)
    setFormOpen(true)
    setForm({
      prompt: item.prompt,
      isRequired: item.isRequired,
      isActive: item.isActive,
      sortOrder: item.sortOrder,
      locationId: item.locationId || null,
    })
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    if (!form.locationId) {
      setFormError('Select a location for this feedback question.')
      return
    }
    setSaving(true)
    setMessage(null)
    setFormError(null)
    try {
      if (editing) await mastersApi.updateFeedbackQuestion(editing.id, form)
      else await mastersApi.createFeedbackQuestion(form)
      setMessage('Saved.')
      closeForm()
      await loadQuestions()
    } catch (err) {
      setFormError(apiErrorMessage(err))
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
      {saving ? <LoadingMask label="Saving question…" /> : null}
      {loading ? <Spinner label="Loading feedback…" /> : null}
      <PageHeader
        title="Visitor feedback"
        subtitle="View submitted star ratings, or manage location-specific questions shown at check-out."
        actions={
          tab === 'questions' ? (
            <div className="flex flex-wrap gap-2">
              <TextSelect
                value={filterLocationId}
                onChange={(e) => setFilterLocationId(e.target.value)}
                className="w-full min-w-[12rem] sm:w-56"
              >
                <option value="">All locations</option>
                {locations.map((l) => (
                  <option key={l.id} value={l.id}>{l.name}</option>
                ))}
              </TextSelect>
              <ExcelImportButtons kind="feedback-questions" label="Feedback questions" onImported={loadQuestions} />
              <Button variant="amber" onClick={startCreate}>
                Add question
              </Button>
            </div>
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
          {!loading ? (
            <Pagination
              page={responsesPage}
              pageSize={responsesPageSize}
              total={responsesTotal}
              onPageChange={setResponsesPage}
              onPageSizeChange={(size) => {
                setResponsesPageSize(size)
                setResponsesPage(1)
              }}
            />
          ) : null}
        </Panel>
      ) : (
        <>
          <Panel className="overflow-x-auto p-0">
            {!loading && items.length === 0 ? (
              <div className="p-4">
                <EmptyState title="No feedback questions" description="Add questions for a location to collect star ratings at check-out." />
              </div>
            ) : null}
            {!loading && items.length > 0 ? (
              <table className="min-w-full text-left text-sm">
                <thead className="bg-gray-50 text-gray-500">
                  <tr>
                    <SortHeader sortKey="order" sort={sort} onSort={onSort}>#</SortHeader>
                    <SortHeader sortKey="prompt" sort={sort} onSort={onSort}>Question</SortHeader>
                    <SortHeader sortKey="location" sort={sort} onSort={onSort}>Location</SortHeader>
                    <SortHeader sortKey="required" sort={sort} onSort={onSort}>Required</SortHeader>
                    <SortHeader sortKey="active" sort={sort} onSort={onSort}>Active</SortHeader>
                    <th className="px-4 py-3" />
                  </tr>
                </thead>
                <tbody>
                  {questionItems.map((item) => (
                    <tr key={item.id} className="border-t border-gray-100">
                      <td className="px-4 py-3">{item.sortOrder}</td>
                      <td className="px-4 py-3 font-medium">{item.prompt}</td>
                      <td className="px-4 py-3">{item.locationName || 'All locations'}</td>
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
            {!loading ? <Pagination {...questionPagination} /> : null}
          </Panel>

          <Modal
            open={formOpen}
            title={editing ? 'Edit question' : 'Add question'}
            onClose={closeForm}
          >
            <form onSubmit={save} className="grid gap-3">
              {formError ? <Alert tone="error">{formError}</Alert> : null}
              <div>
                <FieldLabel htmlFor="fb-location">Location *</FieldLabel>
                <TextSelect
                  id="fb-location"
                  required
                  value={form.locationId || ''}
                  onChange={(e) => setForm({ ...form, locationId: e.target.value || null })}
                >
                  <option value="">Select location</option>
                  {locations.map((l) => (
                    <option key={l.id} value={l.id}>{l.name}</option>
                  ))}
                </TextSelect>
              </div>
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
              <div className="flex justify-end gap-2 border-t border-gray-100 pt-3">
                <Button type="button" variant="secondary" onClick={closeForm}>Cancel</Button>
                <Button type="submit" disabled={saving}>
                  {saving ? 'Saving…' : 'Save'}
                </Button>
              </div>
            </form>
          </Modal>
        </>
      )}
    </div>
  )
}
