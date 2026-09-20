# FusionKick

Standalone BONELAB Fusion lobby-kick mod.

## What it does

Adds a **FusionKick** page to BoneMenu. When Fusion is loaded and you are the Fusion host, the page lists other connected players and creates a **KICK: PlayerName** button for each one.

The mod sends Fusion's own `PermissionSender` **KICK** command. It does not add a second network protocol and it is a completely separate project from Bullet Overdrive.

## Dependencies

- BONELAB
- MelonLoader 0.6.5-compatible runtime
- BoneLib 3.2.2
- BONELAB Fusion 1.14.2 or another Fusion build exposing `LabFusion.Senders.PermissionSender`

## Build

```text
dotnet build FusionKick.csproj -c Release
```

The build produces `FusionKick.dll`.

## Install

Put `FusionKick.dll` in the BONELAB Mods folder. Fusion and BoneLib must also be installed.
