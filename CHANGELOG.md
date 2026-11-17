# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

## [1.0.0] - 2026-11-17

First public release, for SPT 4.1.6.

### Features

- Bullet trajectories drawn from the path the game itself flies, coloured by the real remaining
  penetration or damage at every hit.
- On-screen panel listing every hit of a shot: material, armour level, penetration chance,
  penetration and damage before/after the plate, falloff percentage and whether the surface was
  penetrated — including fragment/child shots of the same bullet.
- Shotguns: one line per pellet, each pellet tracked separately.
- Explosion ranges in three damage bands (full damage / direct hit / any damage), clipped against
  the real geometry of the surroundings.
- Stun ring around explosions, following the vanilla contusion radius and wall absorption.
- Rigid-body flight paths for thrown grenades and flares.
- Grenade throw arc while the pin is out, plus an in-flight blast preview. Both are off by default
  and can affect game balance; see the fair-play notice.
- Two trail display modes: reveal point by point along the ballistic points, or draw the whole
  trajectory the instant the shot is fired.
- Colour mapping for own / Scav / PMC / Boss shots, a configurable stepped colour scale, and a
  colour-swatch picker in the BepInEx configuration window.
- Languages: Auto (system), English, 简体中文, Русский.
- F8 clears everything on screen; the key can be changed.
- Client only: no server component, no network access, no game data modified.
