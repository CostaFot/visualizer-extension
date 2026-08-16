# Microsoft Store listing copy

Compliance-reviewed copy for the Partner Center submission. Structure copied exactly from the
reference repos' shipped listings (MarketExtension → AgentsPanel: one-liner → bullets → key
paragraph → open-source + source link → non-affiliation → disclaimer → Requirements), which went
through Store review twice. Plain text only — Partner Center fields render no markdown. Honest
claims only: the app visualizes what the machine is playing (WASAPI loopback of the *default*
render endpoint — no microphone).

## Short description (search-results summary)

Turn the Command Palette dock into a live audio visualizer.

## Search terms (Partner Center allows 7 × ≤30 chars, ≤21 words total)

1. audio visualizer
2. music visualizer
3. spectrum analyzer
4. oscilloscope
5. equalizer
6. PowerToys
7. Command Palette extension

## Description (main listing field)

Turn the Command Palette dock into a live audio visualizer. Whatever your PC is playing — music, videos, games — shows up as spectrum bars dancing in a pinned dock button.

• Three pinnable dock styles: block bars, a braille dot grid, and bars with a VU-colored peak dot — pin whichever you like from the Dock's band manager
• Click a band for the full in-palette canvas: vertical bars with peak-hold caps, or a retro oscilloscope trace
• Built-in "Test visualizer" command plays its own tone sweep, so you can see it work with nothing queued up
• Uses Windows loopback capture of your default output device — never the microphone
• No recording, no network access, no accounts, no telemetry
• Quiet by design: capture runs only while a visualizer is visible, and it throttles during silence

The audio your machine plays is processed entirely in memory on your device and immediately discarded — nothing is recorded, stored, or transmitted.

This is an open-source, independent extension. Source code is available at https://github.com/CostaFot/visualizer-extension.

It is not affiliated with, endorsed by, or sponsored by Microsoft or the PowerToys team. Company and product names and logos are the property of their respective owners.

Note: the visualizer shows audio reaching your default output device through Windows loopback capture. Audio played in exclusive mode or through protected (DRM) paths does not appear in loopback and cannot be visualized.

Requirements
• PowerToys 0.100 or later, with Command Palette enabled

## Compliance notes

- **Structure copied exactly from the reference repos' shipped listings** — same sections, same
  order, same register (terse opener, tight bullets, key paragraph, "open-source, independent
  extension" + source URL, non-affiliation, note/disclaimer, "Requirements" block last). Both
  passed certification with this shape.
- **Plain text only** — Partner Center description fields render no markdown; the reference
  listings contain none, so neither does this one.
- **Short description matches the description opener verbatim** — same user call recorded in
  AgentsPanel's notes (2026-08-14).
- **Search terms** — 7 terms, 12 words, each ≤30 chars; generic discovery queries ("audio
  visualizer", "spectrum analyzer") plus the platform terms, mirroring AgentsPanel's pattern of
  putting searchable names in terms rather than the short description.
- **Honest capture claims** — "whatever your PC is playing" is accurate for loopback of the
  default render endpoint; the Note paragraph discloses the two real limits
  (exclusive-mode/DRM invisible, default-device-only) so nothing reads as overclaiming.
- **No microphone** — stated explicitly; the package requests no microphone capability, so the
  listing must not imply mic input either.
- **Requirements line** — PowerToys 0.100+ with Command Palette enabled (same line shape the
  reference repos shipped; requirement lives in the store description only, like AgentsPanel —
  NOT in the appxmanifest). Why 0.100: the SDK pin means the minimum host is CmdPal 0.11
  (AGENTS.md), and PowerToys 0.100 is the release shipping CmdPal 0.11 — don't write the CmdPal
  version as a PowerToys version (an earlier draft's "PowerToys 0.11" conflated the two schemes).
- **Trademarks** — nominative use only, plus the explicit non-affiliation line.
- **"Live" is safe here** — unlike MarketExtension's delayed market data, the visualization is
  genuinely local and immediate; no misleading-functionality risk.
