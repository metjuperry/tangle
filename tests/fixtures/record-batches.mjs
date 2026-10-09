import { execFileSync } from 'node:child_process'
import { writeFileSync } from 'node:fs'

const query = `query {
  search(type:ISSUE,query:"repo:TALXIS/platform-metadata is:pr is:open scaffold applier",first:100) {
    pageInfo { hasNextPage }
    nodes { ... on PullRequest { number title createdAt author { login } } }
  }
}`
const data = JSON.parse(execFileSync('gh', ['api', 'graphql', '-f', `query=${query}`], { encoding: 'utf8' })).data.search
if (data.pageInfo.hasNextPage) throw new Error('Fixture needs another page; do not truncate.')
const authors = new Map()
const prs = data.nodes.map(p => {
  if (!authors.has(p.author.login)) authors.set(p.author.login, `person-${authors.size + 1}`)
  return { number: p.number, title: p.title.replace(/pp-[a-zA-Z0-9_-]+/g, `pp-example-${p.number}`),
    createdAt: p.createdAt, author: authors.get(p.author.login) }
})
writeFileSync(new URL('scaffold-batches.json', import.meta.url), JSON.stringify(prs, null, 2) + '\n')
