export function ViewLoading({ destination }: { destination: string }) {
  return <div className="view-loading-overlay" role="status" aria-live="polite">
    <div className="view-loading-paper">
      <svg className="loading-yarn" viewBox="0 0 120 64" fill="none" aria-hidden="true">
        <path className="loading-yarn-track" d="M8 42C24 8 64 8 58 30S24 58 42 30 102 8 112 42" />
        <path className="loading-yarn-thread" d="M8 42C24 8 64 8 58 30S24 58 42 30 102 8 112 42" pathLength="100" />
      </svg>
      <strong>{destination === '/untangle' ? 'Gathering your threads…' : 'Pinning your board…'}</strong>
      <span>{destination === '/untangle' ? 'Getting the deck and receipt ready.' : 'Putting your cards back in view.'}</span>
    </div>
  </div>
}
