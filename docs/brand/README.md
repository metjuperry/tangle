# Tangle logo

The mark is two pins (yours, orange; theirs, blue) joined by a red yarn string tied in a knot, the same thing you do on the board. The wordmark is "tangle" in Bricolage Grotesque, weight 800, letter-spacing -0.02em.

| File | Use |
| --- | --- |
| `tangle-mark.svg` | Mark on light grounds (yarn `#B3262F`) |
| `tangle-mark-on-dark.svg` | Mark on dark grounds such as the ink header (yarn `#E0544B`) |
| `favicon.svg` | Browser tab. Small-size drawing: bigger pins and knot, thicker string, no highlights or wrap line; switches yarn colour with `prefers-color-scheme` |
| `favicon-16.png`, `favicon-32.png` | PNG fallbacks for the favicon |
| `app-icon.svg`, `apple-touch-icon.png` (180), `app-icon-512.png` | Mark on an ink rounded square: home-screen icon and PWA manifest |
| `tangle-mark-512.png` | Mark alone, transparent, for READMEs and slides |
| `tangle-lockup.png`, `tangle-lockup-on-dark.png` | Mark plus wordmark as images; in the app, build the lockup from the SVG and live text instead |

Colours: your pin `#E4572E`, their pin `#2D6BD6`, yarn `#B3262F` on light and `#E0544B` on dark, ink `#221C15`, paper `#FBF6EA`.

In the app header (32–34px), use `tangle-mark-on-dark.svg` next to the wordmark at 24px. Use `favicon.svg` below 24px.

Suggested `index.html` lines:

```html
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<link rel="icon" href="/favicon-32.png" sizes="32x32" type="image/png">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
```

The app serves copies of the mark and icon assets from `web/public/`. The header
uses the dark-ground SVG plus live text in the self-hosted 800-weight font.
`site.webmanifest` defines home-screen name/colors/icons; it does not add offline
support or bypass authentication. Keep the supplied assets and served copies in
sync when changing the brand.
