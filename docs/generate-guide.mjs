import { readFileSync, writeFileSync } from 'node:fs'

const features = JSON.parse(readFileSync(new URL('features.json', import.meta.url), 'utf8'))
const guide = '# How Tangle works\n\nGenerated from `docs/features.json`, the same content used by the in-app Help panel.\n\n' +
  features.map(feature => `## ${feature.title}\n\n${feature.paragraphs.join('\n\n')}\n`).join('\n')
const file = new URL('user-guide.md', import.meta.url)
if (process.argv.includes('--check')) {
  if (readFileSync(file, 'utf8') !== guide) throw new Error('User guide is out of date. Run node docs/generate-guide.mjs.')
} else writeFileSync(file, guide)
