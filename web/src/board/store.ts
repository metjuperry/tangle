import { create } from 'zustand'

type BoardUi = {
  selection: string | null
  stringId: string | null
  dimOthers: boolean
  tieFrom: string | null
  focusRef: string | null
  focus: (reference: string) => void
  startTie: (reference: string | null) => void
  select: (id: string | null) => void
  selectString: (id: string) => void
  toggleOthers: () => void
}
export const useBoardUi = create<BoardUi>(set => ({
  selection: null, stringId: null, dimOthers: false, tieFrom: null, focusRef: null,
  focus: reference => set({ focusRef: reference, selection: reference, stringId: null }),
  startTie: reference => set({ tieFrom: reference }),
  select: id => set({ selection: id, stringId: null }),
  selectString: id => set({ stringId: id, selection: null }),
  toggleOthers: () => set(state => ({ dimOthers: !state.dimOthers })),
}))
