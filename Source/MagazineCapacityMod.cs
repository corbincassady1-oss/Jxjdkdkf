using System;
using System.Collections.Generic;
using BoneLib;
using BoneLib.BoneMenu;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;
using UnityEngine.XR;

[assembly: MelonInfo(typeof(MagazineCapacityMod.Core), "Magazine Capacity Mod", "1.0.0", "OpenAI")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace MagazineCapacityMod
{
    public enum ApplyMode
    {
        AnyHeldMagazine,
        CurrentHeldMagazine
    }

    public enum ActivationInput
    {
        RightB,
        RightThumbstick,
        LeftThumbstick,
        KeyboardB
    }

    public sealed class Core : MelonMod
    {
        private const string ModName = "MagazineCapacityMod";

        private MelonPreferences_Category _prefs;
        private MelonPreferences_Entry<bool> _enabled;
        private MelonPreferences_Entry<int> _capacity;
        private MelonPreferences_Entry<ApplyMode> _mode;
        private MelonPreferences_Entry<ActivationInput> _input;

        private readonly Dictionary<int, int> _originalCapacity = new Dictionary<int, int>();
        private bool _wasPressed;

        public override void OnInitializeMelon()
        {
            SetupPreferences();
            SetupMenu();
            MelonLogger.Msg($"{ModName} loaded.");
        }

        private void SetupPreferences()
        {
            _prefs = MelonPreferences.CreateCategory(ModName);
            _enabled = _prefs.CreateEntry("Enabled", true, "Enable the magazine capacity changer.");
            _capacity = _prefs.CreateEntry("Capacity", 100, "Capacity applied to a held magazine.", validator: new MelonPreferences.ValueRange<int>(1, 999));
            _mode = _prefs.CreateEntry("ApplyMode", ApplyMode.AnyHeldMagazine, "Which magazine is affected.");
            _input = _prefs.CreateEntry("ActivationInput", ActivationInput.RightB, "Controller/keyboard input used to apply the capacity.");
            _prefs.SaveToFile(false);
            MelonPreferences.Save();
        }

        private void SetupMenu()
        {
            Page page = Page.Root.CreatePage("Magazine Capacity", Color.cyan);

            page.CreateBool("Enabled", Color.green, _enabled.Value, value =>
            {
                _enabled.Value = value;
                Save();
            });

            page.CreateInt("Capacity", Color.white, _capacity.Value, 1, 1, 999, value =>
            {
                _capacity.Value = value;
                Save();
            });

            page.CreateEnum("Apply Mode", Color.white, _mode.Value, value =>
            {
                _mode.Value = (ApplyMode)value;
                Save();
            });

            page.CreateEnum("Activation Input", Color.white, _input.Value, value =>
            {
                _input.Value = (ActivationInput)value;
                Save();
            });

            page.CreateFunction("Restore Held Magazine", Color.yellow, RestoreHeldMagazine);
            page.CreateFunction("Save Settings", Color.cyan, Save);
        }

        private void Save()
        {
            _prefs.SaveToFile(false);
            MelonPreferences.Save();
        }

        public override void OnUpdate()
        {
            if (!_enabled.Value)
                return;

            bool pressed = GetActivationPressed();
            if (pressed && !_wasPressed)
            {
                ApplyToHeldMagazines();
            }

            _wasPressed = pressed;
        }

        private bool GetActivationPressed()
        {
            try
            {
                switch (_input.Value)
                {
                    case ActivationInput.KeyboardB:
                        return Input.GetKeyDown(KeyCode.B);

                    case ActivationInput.RightB:
                        return GetXRButton(XRNode.RightHand, CommonUsages.secondaryButton);

                    case ActivationInput.RightThumbstick:
                        return GetXRButton(XRNode.RightHand, CommonUsages.primary2DAxisClick);

                    case ActivationInput.LeftThumbstick:
                        return GetXRButton(XRNode.LeftHand, CommonUsages.primary2DAxisClick);

                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool GetXRButton(XRNode node, InputFeatureUsage<bool> usage)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return false;

            bool value;
            return device.TryGetFeatureValue(usage, out value) && value;
        }

        private void ApplyToHeldMagazines()
        {
            if (_mode.Value == ApplyMode.CurrentHeldMagazine)
            {
                Magazine magazine = GetHeldMagazine();
                if (magazine != null)
                    ApplyCapacity(magazine, _capacity.Value);
                return;
            }

            // "Any Held Magazine" means the physical magazine currently held in either hand.
            // It never edits a prefab, gun default magazine, or global magazine definition.
            Magazine left = Player.GetComponentInHand<Magazine>(Player.LeftHand);
            Magazine right = Player.GetComponentInHand<Magazine>(Player.RightHand);

            if (left != null)
                ApplyCapacity(left, _capacity.Value);

            if (right != null && right != left)
                ApplyCapacity(right, _capacity.Value);
        }

        private Magazine GetHeldMagazine()
        {
            Magazine left = Player.GetComponentInHand<Magazine>(Player.LeftHand);
            if (left != null)
                return left;

            return Player.GetComponentInHand<Magazine>(Player.RightHand);
        }

        private void ApplyCapacity(Magazine magazine, int capacity)
        {
            if (magazine == null || magazine.magazineState == null || magazine.magazineState.magazineData == null)
                return;

            try
            {
                int instanceId = ((Component)magazine).gameObject.GetInstanceID();

                if (!_originalCapacity.ContainsKey(instanceId))
                    _originalCapacity[instanceId] = magazine.magazineState.magazineData.rounds;

                magazine.magazineState.magazineData.rounds = Mathf.Max(1, capacity);

                // Keep the current round count valid when lowering capacity.
                // We deliberately do not refill from reserve, so changing a mag
                // does not silently consume or create inventory ammunition.
                MelonLogger.Msg($"Magazine capacity changed to {capacity}.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to change magazine capacity: {ex}");
            }
        }

        private void RestoreHeldMagazine()
        {
            Magazine magazine = GetHeldMagazine();
            if (magazine == null || magazine.magazineState == null || magazine.magazineState.magazineData == null)
                return;

            try
            {
                int instanceId = ((Component)magazine).gameObject.GetInstanceID();
                int original;

                if (_originalCapacity.TryGetValue(instanceId, out original))
                {
                    magazine.magazineState.magazineData.rounds = original;
                    _originalCapacity.Remove(instanceId);
                    MelonLogger.Msg($"Magazine capacity restored to {original}.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to restore magazine capacity: {ex}");
            }
        }
    }
}