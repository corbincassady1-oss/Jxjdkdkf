# AvaLoader

Entirely separate BONELAB Quest/Standalone code mod.

It scans:

AvaLoader Avatar Folder/

inside BONELAB's persistent files directory.

Example:

AvaLoader Avatar Folder/
  Randomavatar/
    *.pallet.json
    bundles/
    ...

In BoneMenu:

AvaLoader
  Refresh Avatar List
  Load: Randomavatar

Selecting an avatar copies its already-packed BONELAB avatar mod folder into the game's Mods directory and requests an Asset Warehouse reload. If the runtime reload is unavailable, restart BONELAB once.

This loader is for already-packed BONELAB avatar pallets/mod folders. It does not convert raw FBX, VRM, OBJ, or other model files into BONELAB avatar crates. BONELAB's official workflow requires avatars to be built into Avatar Crates and packed as pallets before use.