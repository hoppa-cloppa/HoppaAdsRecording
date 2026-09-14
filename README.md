# HoppaAdsRecording

Unity package that records gameplay touch interactions for the Hoppa ad
creation workflow.

The package itself lives in
[`Packages/com.hoppaplay.adsrecording`](Packages/com.hoppaplay.adsrecording),
and **its README is the documentation** — installation, usage, the output
format and migration notes are all there.

Current version: `1.0.0`

## Install

Package Manager → Add package from git URL:

```
https://github.com/hoppa-cloppa/HoppaAdsRecording.git?path=/Packages/com.hoppaplay.adsrecording#v1.0.0
```

Requires [Unity-DevKit](https://github.com/eliranshaya/Unity-DevKit) and
[UniTask](https://github.com/Cysharp/UniTask) in the consuming project's own
`manifest.json` — Unity cannot resolve Git dependencies declared inside a
package.

## Layout

This repository follows the same shape as Unity-DevKit, Unity-SoundBalance and
Unity-MobileAdjustGameQuality: the package sits under `Packages/` so it can be
installed with `?path=`, and the repository root is free to become a host Unity
project for developing it in place.
