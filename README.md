# Szonszczyk - Definitive Weapon Variants - Client

Better description, download count and comments can be found on: [SPT Forge Link](https://sp-mod.com/mod/2010/definitive-weapon-variants)

Download can be found in [Releases](https://github.com/Szonszczyk/DefinitiveWeaponVariants-csharp/releases)

Client part of [Definitive Weapon Variants](https://github.com/Szonszczyk/DefinitiveWeaponVariants-csharp) server mod for [Single Player Tushonka](https://sp-mod.com/) (SPT - offline mod for Escape from Tarkov) featuring fixes for mod_core slot and adding effects to Variant Weapons, both introduced by DWV

## Features

- Silencing mod_core errors
- Allows inserting and removing Weapon Cores from mod_core slot even when slot is required for weapon to function
- UI Fixes compatibility
- Adding a firing and idle animations for Weapon Variants
- Audio sync for Weapon Variant with changed fire rate
- Coloring Weapon Cores and other DWV introduced items
- Allowing adding pictures for slots - mostly mod_core

## Compatibility

SPT Version: ~4.1.6\
Server mod: ~5.0.0

## Building FirerateAudioSync

Build with `dotnet build -c Release -p:DeployToSpt=false` (set `SptRoot` if needed). Ship `DefinitiveWeaponVariants.Client.dll`, `SoundTouch.Net.dll`, `SoundTouch.Net.LICENSE.txt`, `AssetsTools.NET.dll`, `AssetsTools.NET.LICENSE.txt` and `LICENSE.txt` from `bin/Release/netstandard2.1` together in **`BepInEx/plugins/DefinitiveWeaponVariants`**. The `DeployToSpt=true` build target copies the same complete manifest into the configured game installation and removes the old flat client DLL after a successful copy. The server ZIP obtains that manifest through the client's `GetClientDistributionFiles` target.

SoundTouch.Net 2.3.2 comes from [NuGet](https://www.nuget.org/packages/SoundTouch.Net/2.3.2), with [source maintained by Olaf Woudenberg](https://github.com/owoudenberg/soundtouch.net). It is a managed C# implementation of SoundTouch, originally written by Olli Parviainen. FirerateAudioSync uses it to shorten or lengthen firing loops **without changing their pitch**. Simple playback-speed changes would also alter the sound's pitch. No native DLL is needed; the unmodified dependency and its LGPL license are included in the distribution.

The feature requests `/definitiveweaponvariants/firerate-audio-sync` once at startup. DWV-Server 5.0.0 supplies the catalog; if it is unavailable, the client logs a warning and keeps the original audio. Stock compressed-in-memory PCM16 recordings, including clips embedded in shared sound-bank bundles, are extracted and transformed on background workers, then cached across game sessions. AssetsTools.NET 3.0.5 reads the original bundles without modifying them; its MIT license is included. See the [runtime and cache details](docs/FirerateAudioSync.md#stock-compressed-in-memory-audio).
