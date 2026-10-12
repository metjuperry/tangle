import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, write } from '../api/client'

export function ApplyAccess() {
  const client = useQueryClient()
  const policy = useQuery({ queryKey: ['apply-policy'], queryFn: () =>
    api<{ perUser: boolean; repositories: string[]; sandboxRepo: string | null }>('/api/actions/policy') })
  const save = useMutation({ mutationFn: (allowed: boolean) => write('/api/actions/policy', 'PUT',
    { repositories: allowed && policy.data?.sandboxRepo ? [policy.data.sandboxRepo] : [] }),
  onSuccess: () => client.invalidateQueries({ queryKey: ['apply-policy'] }) })
  if (!policy.data?.perUser) return null
  return <div><p>Opt in to Apply for your account. The server sandbox restriction and per-action confirmation still apply.</p>
    {policy.data.sandboxRepo ? <label className="settings-check"><input type="checkbox" disabled={save.isPending}
      checked={policy.data.repositories.includes(policy.data.sandboxRepo)} onChange={event => save.mutate(event.target.checked)} />
      Allow Apply in {policy.data.sandboxRepo}</label> : <p>No sandbox configured on the server.</p>}
    <p className="quiet">This permission updates immediately, separately from Save settings.</p>
    {save.error && <p role="alert">{save.error.message}</p>}</div>
}
