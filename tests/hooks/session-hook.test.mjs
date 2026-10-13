import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { spawn, execFileSync } from 'node:child_process'
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { isPrCreation } from '../../hooks/pr-created.mjs'

test('PostToolUse filters commands and exact GitHub PR-creation tool names', () => {
  const bash = command => ({ hook_event_name: 'PostToolUse', tool_name: 'Bash', tool_input: { command } })
  for (const command of ['gh pr create --title "Example"', 'cd repo && gh pr create', 'GH_TOKEN=test gh pr create'])
    assert.equal(isPrCreation(bash(command)), true)
  for (const command of ['gh pr list', 'echo "gh pr create"', "printf 'gh pr create'", 'gh api repos/example/repo/pulls', 'gh pr create-example'])
    assert.equal(isPrCreation(bash(command)), false)
  assert.equal(isPrCreation({ hook_event_name: 'SessionEnd', tool_name: 'Bash', tool_input: { command: 'gh pr create' } }), false)
  assert.equal(isPrCreation({ hook_event_name: 'PostToolUse', tool_name: 'mcp__github__create_pull_request' }), true)
  assert.equal(isPrCreation({ hook_event_name: 'PostToolUse', tool_name: 'mcp__github__list_pull_requests' }), false)
  assert.equal(isPrCreation({ hook_event_name: 'PostToolUse', tool_name: 'custom-pr-create' }, 'custom-pr-create'), true)
  assert.equal(isPrCreation({ hook_event_name: 'PostToolUse', tool_name: 'mcp__github__create_pull_request' }, 'custom-pr-create'), false)
})

for (const shell of ['bash', 'pwsh']) {
  test(`${shell} wrapper posts repo/branch metadata and respects excerpt opt-in`, async () => {
    const directory = mkdtempSync(join(tmpdir(), 'tangle-hook-'))
    const received = []
    const server = createServer(async (request, response) => {
      let body = ''
      for await (const chunk of request) body += chunk
      assert.equal(request.headers.authorization, 'Bearer test-personal-key')
      received.push(JSON.parse(body))
      response.writeHead(200, { 'Content-Type': 'application/json' })
      response.end('{}')
    })
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
    try {
      execFileSync('git', ['init', '-b', 'feature/hook', directory], { stdio: 'ignore' })
      execFileSync('git', ['-C', directory, 'remote', 'add', 'origin', 'https://github.com/example/repo.git'])
      const transcript = join(directory, 'session.jsonl')
      writeFileSync(transcript, JSON.stringify({ type: 'user', message: { content: 'First prompt\ncontinued' } }) + '\n')
      for (const excerpt of [false, true]) {
        const args = shell === 'bash' ? [resolve('hooks/tangle-session.sh')] : ['-NoProfile', '-File', resolve('hooks/tangle-session.ps1')]
        const process = spawn(shell, args, { env: { ...globalThis.process.env,
          TANGLE_URL: `http://127.0.0.1:${server.address().port}`, TANGLE_PERSONAL_KEY: 'test-personal-key',
          TANGLE_SESSION_EXCERPT: String(excerpt) } })
        let error = ''
        process.stderr.on('data', chunk => { error += chunk })
        process.stdin.end(JSON.stringify({ hook_event_name: 'PostToolUse', tool_name: 'Bash',
          tool_input: { command: 'gh pr create --title "Fixture"' }, session_id: 'hook-test', cwd: directory, transcript_path: transcript }))
        const exit = await new Promise(resolve => process.on('close', resolve))
        assert.equal(exit, 0, error)
        const report = received.at(-1)
        assert.equal(report.gitBranch, 'feature/hook')
        assert.equal(report.gitRemote, 'https://github.com/example/repo.git')
        assert.equal(report.excerpt, excerpt ? 'First prompt continued' : undefined)
        assert.equal(report.transcriptPath, transcript)
      }

    } finally {
      await new Promise(resolve => server.close(resolve))
      rmSync(directory, { recursive: true })
    }

    test('unrelated tool events skip credentials, Git and HTTP entirely', async () => {
      const child = spawn('node', [resolve('hooks/tangle-session.mjs')], { env: { ...process.env, TANGLE_PERSONAL_KEY: '', TANGLE_URL: 'invalid' } })
      let error = ''
      child.stderr.on('data', chunk => { error += chunk })
      child.stdin.end(JSON.stringify({ hook_event_name: 'PostToolUse', tool_name: 'Read', tool_input: {} }))
      assert.equal(await new Promise(resolve => child.on('close', resolve)), 0, error)
    })
  })
}
