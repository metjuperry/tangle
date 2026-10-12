import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'

export function useLiveEvents() {
  const client = useQueryClient()
  const [status, setStatus] = useState('')
  useEffect(() => {
    const events = new EventSource('/api/events')
    const invalidate = () => {
      void client.invalidateQueries({ queryKey: ['board'] })
      void client.invalidateQueries({ queryKey: ['pr'] })
      void client.invalidateQueries({ queryKey: ['sessions'] })
    }
    for (const type of ['prs.changed', 'verdict.ready', 'link.changed', 'sticky.changed', 'board.reload']) events.addEventListener(type, invalidate)
    events.addEventListener('board.reload', () => { void client.invalidateQueries({ queryKey: ['settings'] }) })
    events.addEventListener('sync.started', () => setStatus('Syncing GitHub…'))
    events.addEventListener('sync.progress', () => setStatus('Refreshing your yarn board…'))
    events.addEventListener('sync.finished', event => {
      const data: { success: boolean } = JSON.parse((event as MessageEvent<string>).data)
      setStatus(data.success ? 'Board up to date.' : 'Sync failed. Cached board retained.')
      invalidate()
      void client.invalidateQueries({ queryKey: ['account'] })
    })
    events.addEventListener('github.access', () => {
      void client.invalidateQueries({ queryKey: ['account'] })
      void client.invalidateQueries({ queryKey: ['apply-policy'] })
    })
    events.addEventListener('ai.budget', () => { setStatus('Monthly AI budget reached. No further provider calls.'); void client.invalidateQueries({ queryKey: ['ai'] }) })
    events.addEventListener('ai.error', event => {
      const data: { error: string } = JSON.parse((event as MessageEvent<string>).data)
      setStatus(`AI suggestions failed: ${data.error}`)
      void client.invalidateQueries({ queryKey: ['ai'] })
    })
    events.addEventListener('verdict.ready', () => { void client.invalidateQueries({ queryKey: ['ai'] }) })
    events.addEventListener('action.changed', () => {
      void client.invalidateQueries({ queryKey: ['actions'] })
      void client.invalidateQueries({ queryKey: ['decisions'] })
      void client.invalidateQueries({ queryKey: ['evaluation'] })
    })
    events.addEventListener('receipt.saved', () => {
      void client.invalidateQueries({ queryKey: ['actions'] })
      void client.invalidateQueries({ queryKey: ['decisions'] })
      void client.invalidateQueries({ queryKey: ['receipt-history'] })
    })
    events.addEventListener('evaluation.ready', () => {
      void client.invalidateQueries({ queryKey: ['evaluation'] })
      void client.invalidateQueries({ queryKey: ['ai'] })
    })
    events.onerror = () => setStatus('Live connection interrupted; reconnecting…')
    events.onopen = () => setStatus('')
    return () => events.close()
  }, [client])
  return status
}
