# FirerateAudioSync prototype

Implemented in DWV-Client 1.0.16 for SPT 4.1.6. The F12 configuration section is **FirerateAudioSync**, with **Enabled = true** by default. This prototype changes audio only. Weapon fire rates and firing modes remain owned by DWV-Server.

## Server route

Register **GET `/definitiveweaponvariants/firerate-audio-sync`** alongside the existing variant-colors and item-colors data routes. Return a JSON object using the same schemaVersion/serverModVersion envelope. The data property is **`firerateAudioSync`**. `RequestHandler.GetJsonAsync` must return the object below to the client parser, without an additional `err/data` wrapper.

```json
{
  "schemaVersion": 1,
  "serverModVersion": "5.0.0",
  "firerateAudioSync": {
    "0123456789abcdef01234567": {
      "sourceTemplateId": "5644bd2b4bdc2d3b4c8b4572",
      "beforeFireRate": 650,
      "afterFireRate": 900
    }
  }
}
```

`0123456789abcdef01234567` is an illustrative generated DWV weapon ID. Replace it with the actual generated 24-hex template ID. The source example identifies the original AK-74N. A standalone copy is in [FirerateAudioSync.example.json](FirerateAudioSync.example.json). **No sourceAudioProfile, profile ID or playbackMode is required from the server.**

| Field | Server meaning |
| --- | --- |
| `schemaVersion` | Integer `1`. |
| `serverModVersion` | Nonempty server mod version string, as on the existing routes. |
| `firerateAudioSync` keys | Actual final DWV weapon template IDs; this is the allowlist. |
| `sourceTemplateId` | Original weapon template ID already known to DWV-Server, as 24 hex characters. Preserve the original through clone-of-clone generation. |
| `beforeFireRate` | Numeric weapon RPM before applying the variant's changes. Capture before mutation. |
| `afterFireRate` | Final numeric RPM from the generated weapon after resolving percentages, minimums and overrides. Send the real value, even if it is 9000; the client applies its audio cap. |

DWV-Server validates the final resolved weapon `BFirerate` before creation: **1 through 10,000 RPM inclusive**, with a warning and skipped variant for invalid, nonfinite or missing effective values. This applies after percentages, minimums and overrides. Changed rates are published regardless of firing mode, including burst-only weapons; the client decides whether their audio needs processing.

The client wire parser remains compatible with finite positive server rates up to 1,000,000; normal DWV generation now enforces the stricter 1–10,000 limit. No percentage strings or nulls. Unknown additive fields are ignored. Duplicate keys or malformed entries reject the entire catalog. An empty `firerateAudioSync: {}` is a valid disabled catalog. The maximum catalog size is 4096 entries. Unsupported source loop timings are detected on the client and skipped for the affected weapon.

The route loads once at game startup, independently of the other DWV data routes, with a 15-second timeout. Missing or invalid responses leave the feature inactive and log a warning. Restart the game after implementing/changing the server response. The F12 toggle can be changed live without another request.

## C# server model

These DTOs show the exact wire contract using System.Text.Json names. Integrate them into your existing RouterModelDWV/envelope classes if those already provide schemaVersion and serverModVersion.

```csharp
using System.Collections.Generic;
using System.Text.Json.Serialization;

public sealed class FirerateAudioSyncResponse
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("serverModVersion")] public string ServerModVersion { get; set; } = "5.0.0";
    [JsonPropertyName("firerateAudioSync")]
    public Dictionary<string, FirerateAudioSyncWeapon> FirerateAudioSync { get; set; } = new();
}

public sealed class FirerateAudioSyncWeapon
{
    [JsonPropertyName("sourceTemplateId")] public string SourceTemplateId { get; set; } = "";
    [JsonPropertyName("beforeFireRate")] public double BeforeFireRate { get; set; }
    [JsonPropertyName("afterFireRate")] public double AfterFireRate { get; set; }
}
```

## Audio identification runs on the client

The server already has all three required fields. Capture the source template ID and the weapon's numeric RPM before changing it, then record the generated weapon's final numeric RPM after percentages, minimums and overrides. Publish those values under the generated weapon's template ID. Include only the DWV weapons you want eligible; ordinary vanilla IDs should not be present.

The client can inspect the actual loaded audio prefab, which the server database cannot reliably classify:

- `WeaponSoundPlayer.Non_auto == false` means the sound player uses loop playback. `Non_auto == true` means per-shot playback, which is left untouched. A weapon's `WeapFireType` is not a reliable substitute: converting a semi-auto weapon to automatic fire does not automatically supply a loop recording.
- For ordinary EFT loop banks, source cadence is derived as `16 * 60 / Body.ClipLength`. It does not assume `beforeFireRate` is the recording's cadence, since another mod could have changed that stat. The requested target remains `afterFireRate`.
- The client creates an internal diagnostic label `auto.<sourceTemplateId>`. You do not need to obtain, assign or send this label. It is not an EFT property or a sound asset identifier.
- Indoor/outdoor, distance and suppressed clips are discovered from the weapon's `Body` and `BodySilenced` banks. No clip names, PathIDs, bundle paths or PCM data are required in the router. `sourceTemplateId` records provenance; it does not load or swap sound assets.

The default detection assumes EFT's ordinary sixteen-beat automatic bank layout. Body and suppressed bank lengths must agree with the derived cadence within 5%; otherwise the client logs a warning and skips the profile. Invalid/unknown timings and detected source cadences above 1800 RPM also retain original audio. Custom banks with different layouts still need explicit authoring/support.

### Optional custom-audio override

For custom audio whose cadence is known, the previous prototype contract remains supported as an optional per-weapon `sourceAudioProfile` object:

```json
"sourceAudioProfile": {
  "id": "my-custom-ak-audio-v1",
  "sourceTemplateId": "5644bd2b4bdc2d3b4c8b4572",
  "sourceFireRate": 650,
  "playbackMode": "loop"
}
```

Here `id` is an arbitrary stable label you choose, at most 128 characters without whitespace. `playbackMode` is exactly `loop` or `perShot` and describes the audio prefab, not weapon firing modes. `sourceFireRate` must describe the original recording. Ordinary cloned weapons do not require this override. If both sourceTemplateId fields are supplied, they must agree. Repeated explicit profile IDs must have identical definitions. The original complete nested-profile payload remains accepted without a top-level sourceTemplateId.

An explicit loop profile is still checked against the body's sixteen-beat bank timing. It does not add support for a different loop layout or a custom audio codec.

## Audio limits

All body recordings, including normal and suppressed distance/environment layers, receive the same effective target RPM. Bank metadata is approximate and individual recordings can have different lengths, so the client computes a separate tempo ratio for each actual sixteen-beat clip:

```text
recordedRpm[i] = 960 / clipDurationSeconds[i]
lower = max(recordedRpm[i] * 0.5)
upper = min(1800, min(recordedRpm[i] * 2.5))
audioRpm = clamp(afterFireRate, lower, upper)
tempoRatio[i] = audioRpm / recordedRpm[i]
newBankDuration = 960 / audioRpm
SoundTouch tempo percent[i] = (tempoRatio[i] - 1) * 100
```

If the recordings have no common supported tempo range, the weapon retains original audio with a warning. The absolute ceiling is 1800 RPM (30 beats per second), with a 2.5x acceleration limit and 0.5x slowdown floor. These deliberate audio approximations do not change ballistics or shot scheduling.

For recordings all authored at 600 RPM, targeting 810 plays at 810; targeting 9000 plays at 1500. For a mixture of 600 and 650 RPM recordings, targeting 9000 still uses 1500 for every layer. The real configured target is retained and each limited weapon logs its real and effective audio RPM once. Per-shot assets follow actual discharge timing without loop processing.

## Runtime behavior and scope

- An eligible player's body banks are replaced together only after **all** required body clips are ready, between firing sequences when the audio queue is empty. Their private `ClipLength` and the sound player's `_firingLoopLength` change together. Suppressor changes subsequently select the matching scaled bank.
- Both enable and disable transitions wait for the current body/tail queue to finish. Vanilla assets, other variants' bank instances, handling sounds and normal reverberation tails are preserved. Tail start timing follows the adjusted beat.
- Original clip/bank identity and tempo ratio key the caches. Two variants sharing an original recording can play at different rates, including on separate AI sound players. Local-only flags are not used. Fika/observed-player behavior still needs in-game verification.
- Doublet/multi-shot recordings are excluded when encountered. Unusual bursts and converted semi-auto weapons need individual checks; the prototype does not manufacture loop assets for them.
- Bundle indexing, resource extraction, PCM decoding, SoundTouch processing and disk cache I/O run on lower-priority background workers. Unity metadata access, readable-clip snapshots, generated clip creation and bank publication run on the main thread. Preparations are serialized, yielding between generated clips. Firing does no decoding, DSP or disk I/O.
- Generated Unity PCM is limited to 128 MiB (with input/output preparation budget checks), individual input clips to 4,194,304 interleaved samples, and generated banks to 128. Cache clearing waits until tracked sound players are gone and their delayed queue release times have passed. Pooled sound players regain their original banks.
- The route is a startup snapshot. Changes to RPM by another client mod later in the raid are outside this prototype; regenerate server data/restart for stat changes.

## Stock compressed-in-memory audio

Since version 1.0.15, the client supports the stock SPT 4.1.6 weapon recordings that triggered the `CompressedInMemory` warnings. Unity refuses `AudioClip.GetData` for that load mode, even though the underlying stock FSB5 resource contains PCM16 samples. `LoadAudioData()` does not change import mode; see [Unity GetData](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioClip.GetData.html).

The client reads those samples directly from the existing UnityFS weapon bundles, using AssetsTools.NET to read bundle contents and a bounded PCM16 reader for FSB5. It matches the live clip's name, channels, frequency and frame count, and requires a unique source. No Unity AssetBundle is loaded, no audio is captured during playback, and no original game asset or bundle is changed. Already-readable custom clips retain the direct Unity `GetData` path.

The index is prepared at startup when enabled and eligible server data exists. It scans both `EscapeFromTarkov_Data/StreamingAssets/Windows/assets/content/audio/weapons` and the adjacent `audio/banks` tree. Some weapons embed their firing clips in shared bank bundles: the MK47 clips are in `audio/banks/mp5.bundle`. Folder names do not establish audio ownership. Index and transformed PCM are cached in **`BepInEx/cache/DefinitiveWeaponVariants/FirerateAudioSync`**. The configured discovery roots and bundle path/size/write time invalidate the index automatically; source resource identity, clip format, DSP version and exact tempo ratio identify transformed PCM. Cache data has a checksum; corrupt entries regenerate automatically. The PCM disk budget is 512 MiB, with oldest entries removed on writes. Cache write failures permit uncached preparation for the current session.

A later game session reads cached PCM without decoding or time stretching it again. Newly requested weapons use original audio until all replacement layers are ready. The cache survives raid cleanup; generated Unity clips do not. Deleting this feature's cache while the game is closed safely forces regeneration.

The extraction path supports the verified stock Unity 2022.3 little-endian AudioClip layout, in-bundle resources and mono/stereo PCM16 FSB5. Missing or ambiguous source matches, external/custom resource layouts and other codecs produce a warning and preserve original banks. No additional DWV-Server fields are required.

## Build and installation verification

```powershell
dotnet build -c Release -p:DeployToSpt=false
```

The output needs **DefinitiveWeaponVariants.Client.dll**, **SoundTouch.Net.dll**, **SoundTouch.Net.LICENSE.txt**, **AssetsTools.NET.dll**, **AssetsTools.NET.LICENSE.txt** and **LICENSE.txt** together in **`BepInEx/plugins/DefinitiveWeaponVariants`**. Local deployment and the server ZIP use the same `GetClientDistributionFiles` manifest. Deployment removes the obsolete flat client DLL only after the new copies succeed.

SoundTouch.Net 2.3.2 is a separate unmodified managed dependency ([NuGet](https://www.nuget.org/packages/SoundTouch.Net/2.3.2), [source](https://github.com/owoudenberg/soundtouch.net)). Olaf Woudenberg's library implements SoundTouch in C#, based on Olli Parviainen's original C++ library. It changes a firing recording's tempo while preserving pitch, which ordinary playback-speed adjustment cannot do. Its LGPL license is copied into build output. No native SoundTouch DLL is needed.

AssetsTools.NET 3.0.5 is an unmodified managed bundle reader from [NuGet](https://www.nuget.org/packages/AssetsTools.NET/3.0.5) and [nesrak1/AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET), distributed with its MIT license.

Restart the game and confirm the startup version is 1.0.16. Start with one ordinary automatic variant at a modest RPM change. Check `BepInEx/LogOutput.log` for `FirerateAudioSync: loaded`, `indexed`, then either `ready` or a specific preparation failure. The ready message reports stock decode and disk-cache hit counts. Verify single shots, short bursts, sustained fire and the final-shot tail. Test the vanilla source and two different variants sharing the same audio, suppressor swaps, F12 toggling during fire, a bot firing simultaneously, and weapon/raid cleanup.

Live raid FPS, audible loop seams and multiplayer behavior still require in-game testing.
