import { useEffect, useState } from 'react'
import { getStoredToken } from '../lib/api'
import { assetUrl, cn } from '../lib/utils'

function isProtectedMedia(src: string): boolean {
  return src.startsWith('/api/media') || src.startsWith('/uploads')
}

export function SecureImage({
  src,
  alt,
  className,
}: {
  src?: string | null
  alt: string
  className?: string
}) {
  const [displaySrc, setDisplaySrc] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let objectUrl: string | null = null
    let cancelled = false
    setFailed(false)
    setDisplaySrc(null)

    async function load() {
      if (!src) {
        setFailed(true)
        return
      }

      if (isProtectedMedia(src)) {
        try {
          const token = getStoredToken()
          const headers: HeadersInit = {}
          if (token) headers.Authorization = `Bearer ${token}`
          const res = await fetch(src, { headers })
          if (!res.ok) throw new Error('Failed to load media')
          const blob = await res.blob()
          if (cancelled) return
          objectUrl = URL.createObjectURL(blob)
          setDisplaySrc(objectUrl)
        } catch {
          if (!cancelled) setFailed(true)
        }
        return
      }

      setDisplaySrc(assetUrl(src))
    }

    void load()

    return () => {
      cancelled = true
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [src])

  if (failed || !src) {
    return (
      <div
        className={cn(
          'flex items-center justify-center bg-gray-100 text-xs text-gray-400',
          className,
        )}
        role="img"
        aria-label={alt || 'No photo'}
      >
        No photo
      </div>
    )
  }

  if (!displaySrc) {
    return <div className={cn('bg-gray-50', className)} aria-hidden />
  }

  return (
    <img
      src={displaySrc}
      alt={alt}
      className={className}
      onError={() => setFailed(true)}
    />
  )
}
