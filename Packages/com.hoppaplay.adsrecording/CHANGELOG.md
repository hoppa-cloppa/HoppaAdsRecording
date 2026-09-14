# Changelog

All notable changes to this package are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-09-14

First release. Extracted from the Pinata Blast project, where this system was
developed in place, so that any Hoppa game can record ads without copying
scripts between projects.

### Added
- `AdsRecording` — the whole package as one static facade: `Start`, `Stop`,
  `StopAndShare`, `Discard`, `IsRecording`, `EventCount`, `Describe`.
  Previously the start and stop SEQUENCES lived in the game's own developer
  component, which meant the next game to adopt recording had to reproduce a
  specific order of operations correctly from reading someone else's code.
- `RecordingUiMask.SetHiddenObjectNames` so a game can name its own HUD objects
  instead of inheriting Pinata Blast's hierarchy.

### Changed
- Classes renamed from `PIB*` and moved to the `HoppaPlay.AdsRecording`
  namespace. See the README for the mapping.
- `TouchRecorder` derives from `MonoBehaviour` rather than the game's
  `PIBMonoBehaviour`. It used nothing from that base class, so behaviour is
  unchanged.

### Unchanged, deliberately
- **The recording JSON format.** The ad video editor reads it, so the
  serializer was moved verbatim. Recordings made with the old scripts and with
  this package are byte-for-byte equivalent.
- Touch capture, UI masking, sync flash, share and upload behaviour.
