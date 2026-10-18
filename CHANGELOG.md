# Changelog

## [1.1.0]

[GitHub release](https://github.com/EklipZgit/BeatSaberChromaGLS/releases/tag/v2026-10-17-1.1.0)

### Added

- `colorDistributions` and `strobeColorDistributions` on GLS color events and event boxes, with independent normal/strobe channels and RGB, HSV, and alpha offsets.
- Custom incoming transition `easing`, `colorEasing`, and `strobeColorEasing`, plus shortest-path HSV color interpolation through `easingType: "HSV"`.
- `strobePulseWidth` for asymmetric hard strobes and faded pulses, and `strobeEasing` for the fade curve within each pulse.
- Interactive ferrofluid around SongCore's info button for ChromaGLS maps.
- A ChromaGLS settings tab, where you can optionally add ferrofluid cube and sphere fidget toys to your main menu~
- Check for updates at startup and provide a link to download latest when out of date.

### Fixed

- GLS transitions now use the next event for each affected light, including across event boxes, rather than mixing endpoints from unrelated lights.
- Extend nodes retain each light's previous colors, distributions, and strobe settings without inheriting the predecessor's incoming transition easing.
- Corrected transitions between custom `strobeInterval` values, native strobe frequencies, and non-strobing events, including unwanted brightness snaps and lingering strobe colors.
- Explicit linear color and pulse easing values are honored independently of the interval easing. Strobe-specific distributions no longer pick up normal-channel offsets when they define their own channel.
- Tube-bloom reflections and their collision hit points refresh their colors while active instead of retaining an earlier GLS color on game versions that support those effects.
- Removed the environment gradient on maps requiring or suggesting ChromaGLS when Chroma is not already responsible for removing it.

## [1.0.3] - 2026-08-31

[GitHub release](https://github.com/EklipZgit/BeatSaberChromaGLS/releases/tag/v2026-08-31-1.0.3)

- Adds support for arbitrary strobeInterval in customData, so you can do non-fractional strobe intervals instead of being locked to specific fractions of a beat, and can make strobes slower than 1 beat etc.
- HDR color support. Try applying colors like [3.0, 0, 0, 0.3] etc. Better yet, try strobing to it from a standard red color. Have fun!
- Shifted backport out to [BeatToTheFuture](https://github.com/EklipZgit/BeatToTheFuture/releases#release-untagged-336b9328e2d851a9eebd) mod and took a dependency on it, so when people search BeatSaver for ChromaGLS maps they actually find colorful maps instead of just people who used the backport tools that are now in BeatToTheFuture.

## [1.0.2] - 2026-07-22

[GitHub release](https://github.com/EklipZgit/BeatSaberChromaGLS/releases/tag/v2026-07-22-1.0.2)

- Fixed dumb mistake in The Second Environment's ring float control that multiplied by "i"

## [1.0.1] - 2026-07-22

[GitHub release](https://github.com/EklipZgit/BeatSaberChromaGLS/releases/tag/v2026-07-22)

Initial Github release - 1.0.1.

- Adds support for Chroma colors to GLS color events. Adds customData.color and customData.strobeColor support (to strobe between two colors).
- Exactly preserves existing GLS behavior, but with arbitrary color.
- Adds floating point control to The Second Environments wonky post-basic-event pre-gls-translation-event Ring Zoom (via customData.step just like Heck/Chroma for v2 envs).
- 1.44.1 support via 1.42.1 plugin.
