# Swarmfall Tower

A 3D survivor game for Windows, modelled on bonk games. Explore three open zones while your moves fire on their
own, catch wild creatures to fight beside you, and climb to the final boss.

## Play

1. Download [`Swarmfall.exe`](Swarmfall.exe) (open it, then use the download button), or clone this repository.
2. Double-click `Swarmfall.exe`.

It needs no install and no internet. It only needs the .NET Framework 4 and OpenGL, which are part of Windows.
Windows may warn that the file is from an unknown publisher the first time you run it.

## Controls

WASD move, mouse camera, Space jump, Shift slide (or dive in the air), R ultimate, E interact, Esc pause, M mute,
F11 fullscreen. Q and C also turn the camera.

## How a run works

Three zones, each on a large map with a 4-minute timer. The guardian arrives at 0:00, or earlier if you challenge
it at the guardian stone for bonus gold. Beating it opens a portal. The third zone ends with a final boss.

Level-ups offer move upgrades, new moves and vitamins, with limited Refresh, Skip and Banish uses. Items come from
chests, the merchant and guardians, and most stack. Two level 8 moves can fuse at a fusion machine: the partner's
signature pair first, then an element reaction, then a shared element, otherwise Glitch.

Wild creatures appear during a zone and keep their distance. Beating one weakens it, and you then have 12 seconds
to get close and throw a capture orb with E. A catch fights beside you for that run and gives a bonus based on its
type, with up to 2 allies at a time.

## Creatures

The first time you play, choose **Chichar** (Volt), **Fugiri** (Tide) or **Toyat** (Stone). The one you pick is
the only creature unlocked at the start. Every other creature is earned: complete its challenge, then catch it in
the wild, and it joins your roster for good. The menu shows each creature's challenge and progress.

- **Chichar** evolves into Bijiban at level 10 and Chamzahar (Volt/Venom) at level 22.
- **Fugiri** evolves into Yugiri, then Yuninjo (Tide/Iron).
- **Toyat** evolves into Eytetl, then Xutetl (Stone/Bloom).
- **Furrowkit** evolves into Burrowbat, then Borrog (Flame/Stone).
- **Quilven**, a Basic-type slime, changes into one of 8 typed forms at level 10.
- Eight more creatures, met as enemies first, can join the roster too.

## Modes and difficulty

- **Classic** is the three-zone climb. **Survival** is one endless map with a boss every 5 minutes; after
  45 minutes the Endless Night makes sure every run ends. Your best time is saved.
- **Difficulty:** Normal, Hard and Nightmare. Harder settings give more Shards.
- **Types:** Flame, Tide, Bloom, Volt, Stone, Iron, Venom, Spirit and Basic. Matchups do 2x, half or nothing, and
  dual types multiply. Some moves react with each other, such as Tide then Volt for Shock.

## The Lab

Runs earn Shards, kept even if you quit early. The Lab tab on the menu spends them on permanent upgrades to stats
and run tools, such as a fourth move slot and a revive.

## Saves

Progress saves automatically to `%APPDATA%\SwarmfallTower\save.txt`, with the previous version kept as
`save.txt.bak`. Replacing `Swarmfall.exe` with a newer version keeps your save. A damaged save is set aside rather
than deleted.

## Engine source

`native/` holds the C# source of the included `Swarmfall.exe` (a small custom OpenGL 1.1 engine), published for
reference. This repository doesn't include a way to build it.

## Rights

Swarmfall Tower belongs to its owner. No open-source license has been granted: you may download and play the game,
but not redistribute or reuse the game, its source or its art without permission.
