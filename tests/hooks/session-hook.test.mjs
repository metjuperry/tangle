import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { spawn, execFileSync } from 'node:child_process'
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'

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
        process.stdin.end(JSON.stringify({ session_id: 'hook-test', cwd: directory, transcript_path: transcript }))
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
  })
}
