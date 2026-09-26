# HealthRegenToggle Customizable

Modified from the original HealthRegenToggle behavior.

BoneMenu settings:
- Enable Regeneration
- Enable Damage Vignette
- Healing Delay: 0–30 seconds
- Regen Speed: 0.1–20x
- Health Regen: 0–100 HP per second at 1x speed

Default: 2s delay, 1x speed, 5 HP/s.

The mod uses JLib/BoneLib at runtime and reflection for BONELAB's generated health types so the build does not hard-bind to a desktop-only Assembly-CSharp reference.
