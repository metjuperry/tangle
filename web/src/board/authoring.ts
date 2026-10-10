import { useMutation, useQueryClient } from '@tanstack/react-query'
import { write, type Yarn } from '../api/client'
import { useBoardUi } from './store'

export function useTie() {
  const client = useQueryClient()
  const selectString = useBoardUi(s => s.selectString)
  const startTie = useBoardUi(s => s.startTie)
  return useMutation({
    mutationFn: ({ aRef, bRef }: { aRef: string; bRef: string }) => write<Yarn>('/api/links', 'POST', { aRef, bRef }),
    onSuccess: async link => {
      startTie(null)
      await client.invalidateQueries({ queryKey: ['board'] })
      await client.invalidateQueries({ queryKey: ['pr'] })
      selectString(link.id)
    },
  })
}
