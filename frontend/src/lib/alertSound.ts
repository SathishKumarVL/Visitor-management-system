// Chrome loads its voice list in the background; asking early means it is ready when the first alert speaks.
if (typeof window !== 'undefined') window.speechSynthesis?.getVoices()

/** Browsers do not say which voices are female, so known female voice names are matched instead. */
const FEMALE_VOICE_NAMES = [
  'neerja', 'heera', 'veena', 'isha', 'zira', 'aria', 'jenny', 'michelle', 'sonia', 'libby', 'hazel', 'susan',
  'samantha', 'karen', 'moira', 'tessa', 'female',
]

function isFemale(voice: SpeechSynthesisVoice): boolean {
  const name = voice.name.toLowerCase()
  return FEMALE_VOICE_NAMES.some((n) => name.includes(n))
}

/**
 * Speaks an alert aloud with the browser's built-in text-to-speech, so it needs no sound file and works
 * offline. Uses a female voice, Indian English first, when the computer has one. Browsers only allow
 * speech after the user has clicked or typed on the page.
 */
export function speakAlert(message: string): void {
  try {
    const synth = window.speechSynthesis
    if (!synth) return
    synth.cancel()
    const utterance = new SpeechSynthesisUtterance(message)
    const english = synth.getVoices().filter((v) => v.lang.startsWith('en'))
    const voice =
      english.find((v) => v.lang === 'en-IN' && isFemale(v)) ??
      english.find(isFemale) ??
      english.find((v) => v.lang === 'en-IN') ??
      english[0] ??
      null
    if (voice) utterance.voice = voice
    if (!voice || !isFemale(voice)) utterance.pitch = 1.3
    utterance.lang = voice?.lang ?? 'en-IN'
    utterance.rate = 0.95
    utterance.volume = 1
    synth.speak(utterance)
  } catch {
    /* ignore */
  }
}

/** "Alert. A different person has come for RAKSHAN - Class 1st-A. Please check with the parent before registering." */
export function newPersonAlertText(display: string): string {
  return `Alert. A different person has come for ${display}. Please check with the parent before registering.`
}
