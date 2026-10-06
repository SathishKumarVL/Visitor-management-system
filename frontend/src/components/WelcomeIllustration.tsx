type Props = { className?: string }

export function WelcomeIllustration({ className }: Props) {
  return (
    <svg viewBox="0 0 320 200" className={className} role="img" aria-hidden="true">
      <ellipse cx="160" cy="186" rx="132" ry="9" className="fill-mint" />
      <circle cx="160" cy="98" r="88" className="fill-aqua-light" opacity="0.7" />

      {/* doorway */}
      <rect x="108" y="36" width="104" height="148" rx="12" className="fill-white stroke-primary" strokeOpacity="0.25" strokeWidth="3" />
      <rect x="118" y="46" width="84" height="136" rx="8" className="fill-primary" opacity="0.1" />
      <path d="M202 46 L238 36 L238 194 L202 182 Z" className="fill-primary" />
      <circle cx="230" cy="118" r="3.5" className="fill-white" />
      <rect x="114" y="180" width="92" height="8" rx="4" className="fill-primary-dark" />

      {/* host waving from the doorway */}
      <rect x="149" y="150" width="9" height="30" rx="4" className="fill-ink" />
      <rect x="162" y="150" width="9" height="30" rx="4" className="fill-ink" />
      <rect x="143" y="104" width="34" height="54" rx="15" className="fill-accent" />
      <path d="M146 114 L136 142" strokeWidth="8" strokeLinecap="round" stroke="#F1C7A1" />
      <path d="M174 114 L192 86" strokeWidth="8" strokeLinecap="round" stroke="#F1C7A1" />
      <circle cx="194" cy="82" r="6" fill="#F1C7A1" />
      <circle cx="160" cy="90" r="13" fill="#F1C7A1" />
      <path d="M147 88 C147 74 173 72 173 88 C168 82 154 81 147 88 Z" className="fill-ink" />
      <path d="M155 95 Q160 99 165 95" strokeWidth="2" strokeLinecap="round" fill="none" className="stroke-ink" />

      {/* plant */}
      <path d="M74 150 C60 130 62 112 70 104 C78 116 80 134 74 150 Z" className="fill-primary" opacity="0.8" />
      <path d="M78 150 C82 128 94 118 104 116 C102 132 92 146 78 150 Z" className="fill-accent" />
      <path d="M72 152 C58 144 50 132 50 122 C62 126 72 138 72 152 Z" className="fill-primary-dark" opacity="0.7" />
      <rect x="62" y="150" width="28" height="30" rx="5" className="fill-primary-dark" />

      {/* sparkles */}
      <path d="M86 58 l3 8 l8 3 l-8 3 l-3 8 l-3 -8 l-8 -3 l8 -3 Z" fill="#F5B942" />
      <path d="M252 64 l2 6 l6 2 l-6 2 l-2 6 l-2 -6 l-6 -2 l6 -2 Z" fill="#F5B942" />
      <path d="M264 128 l2.5 6 l6 2.5 l-6 2.5 l-2.5 6 l-2.5 -6 l-6 -2.5 l6 -2.5 Z" className="fill-accent" />
      <circle cx="104" cy="84" r="3" className="fill-accent" />
      <circle cx="244" cy="100" r="2.5" className="fill-primary" />
    </svg>
  )
}
