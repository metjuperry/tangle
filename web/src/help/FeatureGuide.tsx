import { useEffect, useRef } from 'react'
import features from '../../../docs/features.json'

export function FeatureGuide({ onClose }: { onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => { dialog.current?.showModal() }, [])
  return <dialog ref={dialog} className="new-sticky-dialog feature-guide" aria-labelledby="feature-guide-heading" onCancel={onClose}>
    <div className="history-heading"><h2 id="feature-guide-heading">How Tangle works</h2><button onClick={onClose}>Close</button></div>
    <p>Follow the cards, decide what needs you, and keep control of what leaves the app.</p>
    {features.map(feature => <details key={feature.id} className="feature-section">
      <summary>{feature.title}</summary>{feature.paragraphs.map((paragraph, i) => <p key={i}>{paragraph}</p>)}
    </details>)}
  </dialog>
}
