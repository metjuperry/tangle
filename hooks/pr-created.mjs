export function isPrCreation(input, configuredTool = '') {
  if (input.hook_event_name !== 'PostToolUse') return false
  const name = input.tool_name
  if (typeof name !== 'string') return false
  if (configuredTool ? name === configuredTool : /^mcp__[^_]+(?:_[^_]+)*__create_pull_request$/.test(name)) return true
  if (name !== 'Bash' || typeof input.tool_input?.command !== 'string') return false

  // Keep quoted strings intact so examples inside echo/printf are not commands.
  const tokens = input.tool_input.command.match(/"(?:\\.|[^"\\])*"|'[^']*'|&&|\|\||[;|\n]|[^\s;|"']+/g) ?? []
  let start = true
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i]
    if (['&&', '||', ';', '|', '\n'].includes(token)) { start = true; continue }
    if (!start) continue
    if (/^[A-Za-z_][A-Za-z0-9_]*=/.test(token)) continue
    if (token === 'command') continue
    start = false
    if (token === 'gh' && tokens[i + 1] === 'pr' && tokens[i + 2] === 'create') return true
  }
  return false
}
