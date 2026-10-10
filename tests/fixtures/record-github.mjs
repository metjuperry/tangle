import { execFileSync } from 'node:child_process'
import { readFileSync, writeFileSync } from 'node:fs'

// Run only when intentionally refreshing public read-only fixtures; never from tests.
const query = readFileSync(new URL('../../src/Tangle.GitHub/queries/Prs.graphql', import.meta.url), 'utf8')
const response = JSON.parse(execFileSync('gh', ['api', 'graphql', '-f', `query=${query}`, '-f',
  'q=is:pr is:open repo:TALXIS/alm-lab involves:metjuperry'], { encoding: 'utf8' }))
const logins = new Map()
const paths = new Map()
const identifiers = new Map()
let nextLogin = 0
function login(value) {
  if (value === 'metjuperry') return 'viewer'
  if (!logins.has(value)) logins.set(value, `person-${++nextLogin}`)
  return logins.get(value)
}
function scrub(node) {
  if (Array.isArray(node)) return node.map(scrub)
  if (node === null || typeof node !== 'object') return node
  const result = {}
  for (const [key, value] of Object.entries(node)) {
    if (typeof value !== 'string') result[key] = scrub(value)
    else if (key === 'login') result[key] = login(value)
    else if (key === 'name') result[key] = 'Example commit author'
    else if (key === 'nameWithOwner') result[key] = 'example/repo'
    else if (['body', 'bodyText', 'message'].includes(key)) result[key] = 'Scrubbed public fixture text.'
    else if (key === 'title') result[key] = 'Example pull request'
    else if (key === 'url') result[key] = 'https://github.com/example/repo/pull/49'
    else if (key === 'path') {
      if (!paths.has(value)) paths.set(value, `src/example-${paths.size + 1}.txt`)
      result[key] = paths.get(value)
    }
    else if (['id', 'startCursor', 'endCursor'].includes(key)) {
      if (!identifiers.has(value)) identifiers.set(value, `fixture-id-${identifiers.size + 1}`)
      result[key] = identifiers.get(value)
    }
    else if (['headRefName', 'baseRefName', 'slug'].includes(key)) result[key] = key === 'baseRefName' ? 'main' : 'example'
    else result[key] = value
  }
  return result
}
const fixture = scrub(response)
fixture.data.search.nodes = fixture.data.search.nodes.slice(0, 1)
fixture.data.search.issueCount = fixture.data.search.nodes.length
fixture.data.search.pageInfo = { hasNextPage: false, endCursor: null }
writeFileSync(new URL('github-pr-page.json', import.meta.url), JSON.stringify(fixture, null, 2) + '\n')
