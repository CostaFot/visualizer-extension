# Certification test notes (Partner Center "Notes for certification" field)

Paste-ready text for the submission. The key point for a reviewer: they will have no music
playing — the built-in **Test visualizer** command makes the app fully self-demonstrating with
zero accounts, zero credentials, and zero content.

---

## Notes for certification

**What this app is:** an extension for Microsoft PowerToys Command Palette that displays a live
audio spectrum visualization of whatever the machine is playing (Windows WASAPI loopback capture
of the default output device — it never accesses the microphone). It has no accounts, no
sign-ins, no network access, and includes a built-in test signal so it can be fully tested with
no audio content available.

**Prerequisite:** Microsoft PowerToys 0.100 or later with Command Palette enabled
(install from the Microsoft Store or https://aka.ms/getPowertoys).

**Steps to test:**

1. Install PowerToys (0.100+) and this package. Open Command Palette (default hotkey: Win+Alt+Space).
2. Type "Visualizer" — the extension's hub appears. Open it to see its menu: the canvas
   visualizer, the "Test visualizer" command, a volume-mixer shortcut, and settings.
3. Run **"Test visualizer"** (a row in the hub). The app plays its own built-in test tone —
   an ascending tone ladder followed by a frequency sweep — and any visible visualizer surface
   lights up in response. No music, media files, or accounts are needed.
4. Open the canvas page (the hub's visualizer row) while the test signal plays: an animated
   bar-spectrum drawn as text fills the page. Its style (bars / oscilloscope) can be switched in
   the extension's settings.
5. Dock integration: in Command Palette, open the Dock's band manager and pin any of the three
   "Visualizer" bands (block bars, braille, VU dot). The pinned dock button animates while
   audio plays; clicking it opens the canvas page; right-click offers "Test visualizer" and the
   volume mixer.
6. With no audio playing, the visualizer intentionally idles at a quiet floor (flat bars) and
   lowers its refresh rate — this is by design, not a malfunction. It resumes instantly when
   audio plays.

**runFullTrust justification:** the app is a PowerToys Command Palette extension. The Command
Palette host activates extensions exclusively as packaged out-of-process COM servers
(`windows.comServer` + `com.microsoft.commandpalette` appExtension), which requires the
`runFullTrust` capability. This is the standard mechanism used by all Command Palette
extensions; the app declares no other capabilities (in particular, no microphone).

**Privacy:** audio is processed entirely in memory and never recorded, stored, or transmitted;
the app makes no network requests at all. Privacy policy:
https://costafot.github.io/visualizer-extension/privacy.html

---

Notes to self (not for the reviewer):

- The privacy URL above was verified live 2026-08-16 (Pages enabled + deployed; all three pages
  and style.css serve 200, and privacy.html serves the real policy).
