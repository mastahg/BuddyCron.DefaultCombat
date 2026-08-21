# BuddyCron.DefaultCombat

Default combat routines for BuddyCron, the SWTOR bot.

## Included

- Shared casting, targeting, movement, recovery, and hotkey behaviors.
- Advanced discipline routines under `Routines/Advanced`.
- DPS, healing, and tank rotations.

## Build

Open `DefaultCombat.csproj` with the BuddyCron reference package available. Build for `x64` with .NET 10.

BuddyCron compiles these routines from source. Restart BuddyCron after changing files.

## Notes

Rotation behavior depends on the character's level, discipline, abilities, and talents. Test changes in-game before unattended use.

See `LICENSE.txt` for licensing details.
