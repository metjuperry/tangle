import { create } from 'zustand'

type BoardUi = {
  selection: string | null
  stringId: string | null
  dimOthers: boolean
  tieFrom: string | null
  startTie: (reference: string | null) => void
  select: (id: string | null) => void
  selectString: (id: string) => void
  toggleOthers: () => void
}
export const useBoardUi = create<BoardUi>(set => ({
  selection: null, stringId: null, dimOthers: false, tieFrom: null,
  startTie: reference => set({ tieFrom: reference }),
  select: id => set({ selection: id, stringId: null }),
  selectString: id => set({ stringId: id, selection: null }),
  toggleOthers: () => set(state => ({ dimOthers: !state.dimOthers })),
}))
