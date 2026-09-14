import { useEffect, useRef, useState } from 'react'
import { Html5Qrcode } from 'html5-qrcode'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { apiErrorMessage, passApi, visitorsApi } from '../lib/api'
import type { PassDto } from '../types/api'
import { Alert, PageHeader, Panel } from '../components/ui/Panel'
import { Button } from '../components/ui/Button'
import { TextInput, FieldLabel } from '../components/ui/Field'

const REGION_ID = 'tiaano-qr-reader'

function statusKey(status: string): string {
  return status.toLowerCase()
}

export function QrScanPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const checkoutMode = searchParams.get('mode') === 'checkout'
  const scannerRef = useRef<Html5Qrcode | null>(null)
  const lastHandledRef = useRef<string>('')
  const [scanning, setScanning] = useState(false)
  const [manual, setManual] = useState('')
  const [pass, setPass] = useState<PassDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    return () => {
      void stopScanner()
    }
  }, [])

  useEffect(() => {
    if (!checkoutMode) return
    void startScanner()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [checkoutMode])

  async function stopScanner() {
    const scanner = scannerRef.current
    if (!scanner) return
    try {
      if (scanner.isScanning) await scanner.stop()
      scanner.clear()
    } catch {
      /* ignore */
    }
    scannerRef.current = null
    setScanning(false)
  }

  async function startScanner() {
    setError(null)
    await stopScanner()
    const scanner = new Html5Qrcode(REGION_ID)
    scannerRef.current = scanner
    try {
      await scanner.start(
        { facingMode: 'environment' },
        { fps: 10, qrbox: { width: 240, height: 240 } },
        (decoded) => {
          void handleCode(decoded)
        },
        () => undefined,
      )
      setScanning(true)
    } catch {
      setError('Unable to start camera scanner. Enter the pass code manually.')
      setScanning(false)
    }
  }

  async function handleCode(code: string) {
    const passCode = code.trim()
    if (!passCode) return
    if (busy) return
    if (lastHandledRef.current === passCode) return
    lastHandledRef.current = passCode
    setBusy(true)
    setError(null)
    setMessage(null)
    try {
      await stopScanner()
      const result = await passApi.scan(passCode)
      setPass(result)
      const status = statusKey(result.status)

      if (checkoutMode) {
        if (status.includes('inside')) {
          await visitorsApi.checkOut(result.visitId)
          setPass(null)
          setMessage(`Checked out: ${result.visitorName}. Out time recorded.`)
          lastHandledRef.current = ''
        } else if (status.includes('approved') || status.includes('expected')) {
          setMessage(`${result.visitorName} is not checked in yet. Check in first, then use Check Out.`)
        } else if (status.includes('checkedout') || status.includes('checked out')) {
          setMessage(`Already checked out: ${result.visitorName}`)
        } else {
          setMessage(`Cannot check out ${result.visitorName} (status: ${result.status}).`)
        }
      } else if (status.includes('inside')) {
        await visitorsApi.checkOut(result.visitId)
        setPass(null)
        setMessage(`Checked out: ${result.visitorName}. Out time recorded.`)
        lastHandledRef.current = ''
      } else if (status.includes('approved') || status.includes('expected')) {
        const updated = await visitorsApi.checkIn(result.visitId)
        setPass(updated)
        setMessage(`Checked in: ${result.visitorName}`)
      } else if (status.includes('checkedout') || status.includes('checked out')) {
        setMessage(`Visitor already checked out: ${result.visitorName}`)
      } else {
        setMessage(`Pass verified: ${result.visitorName} (${result.status})`)
      }
    } catch (e) {
      setPass(null)
      setError(apiErrorMessage(e, 'INVALID VISITOR PASS'))
      lastHandledRef.current = ''
    } finally {
      setBusy(false)
    }
  }

  async function checkOutFromPass() {
    if (!pass?.visitId) return
    setBusy(true)
    setError(null)
    try {
      await visitorsApi.checkOut(pass.visitId)
      setMessage(`Checked out: ${pass.visitorName}. Out time recorded.`)
      setPass(null)
      lastHandledRef.current = ''
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  async function checkInFromPass() {
    if (!pass?.visitId) return
    setBusy(true)
    setError(null)
    try {
      const updated = await visitorsApi.checkIn(pass.visitId)
      setPass(updated)
      setMessage(`Checked in: ${updated.visitorName}`)
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const status = pass ? statusKey(pass.status) : ''
  const canCheckIn = !checkoutMode && (status.includes('approved') || status.includes('expected'))
  const canCheckOut = status.includes('inside')

  return (
    <div>
      <PageHeader
        eyebrow="Verification"
        title={checkoutMode ? 'Check Out' : 'Scan Visitor Pass'}
        subtitle={
          checkoutMode
            ? 'Center the pass QR in the frame to record out time'
            : 'Scan visitor pass for check-in and check-out'
        }
        actions={
          checkoutMode ? (
            <Button variant="secondary" onClick={() => navigate('/reception')}>
              Back to Reception
            </Button>
          ) : null
        }
      />

      <div className="grid gap-4 lg:grid-cols-2">
        <Panel>
          <p className="mb-3 text-center text-sm font-medium text-ink-muted">Scan visitor pass</p>
          <div
            id={REGION_ID}
            className="mx-auto aspect-square max-w-sm overflow-hidden rounded-2xl bg-ink ring-4 ring-aqua-light"
            aria-label="QR scanner viewfinder"
          />
          <div className="mt-3 flex flex-wrap justify-center gap-2">
            {!scanning ? (
              <Button variant="primary" onClick={() => void startScanner()}>
                Start scanner
              </Button>
            ) : (
              <Button variant="secondary" onClick={() => void stopScanner()}>
                Stop scanner
              </Button>
            )}
          </div>
          <div className="mt-4 border-t border-border/70 pt-4">
            <FieldLabel htmlFor="manual">Manual visitor / pass ID</FieldLabel>
            <div className="flex gap-2">
              <TextInput
                id="manual"
                value={manual}
                onChange={(e) => setManual(e.target.value)}
                placeholder="Enter pass code"
              />
              <Button disabled={busy || !manual.trim()} onClick={() => void handleCode(manual)}>
                Lookup
              </Button>
            </div>
          </div>
        </Panel>

        <Panel>
          {error ? <Alert tone="error">{error.includes('INVALID') || error.toLowerCase().includes('not found') ? 'Invalid / Expired Pass' : error}</Alert> : null}
          {message ? (
            <div className="mb-3">
              <Alert tone="success">{message.startsWith('Checked') || message.startsWith('Pass verified') ? `Visitor Verified — ${message}` : message}</Alert>
            </div>
          ) : null}
          {pass ? (
            <div className="space-y-2 text-sm">
              <div className="text-xl font-semibold text-ink">{pass.visitorName}</div>
              <div className="text-ink-muted">{pass.companyName}</div>
              <div>Host: {pass.hostName}</div>
              <div>Department: {pass.departmentName}</div>
              <div>Status: {pass.status}</div>
              <div className="font-mono text-primary">Pass: {pass.passCode}</div>
              <div className="flex flex-wrap gap-2 pt-3">
                <Button onClick={() => navigate(`/visitors/${pass.visitId}`)}>Open visitor</Button>
                {canCheckIn ? (
                  <Button disabled={busy} onClick={() => void checkInFromPass()}>
                    Check in
                  </Button>
                ) : null}
                {canCheckOut ? (
                  <Button variant="danger" disabled={busy} onClick={() => void checkOutFromPass()}>
                    Check out
                  </Button>
                ) : null}
              </div>
            </div>
          ) : (
            <p className="text-sm text-ink-muted">
              {checkoutMode
                ? 'Scan the visitor QR. If they are currently inside, out time is recorded immediately.'
                : 'Align the printed QR inside the frame. Successful scans show verification details here.'}
            </p>
          )}
        </Panel>
      </div>
    </div>
  )
}
