using System;
using System.Collections.Generic;
using BoneLib;
using BoneLib.BoneMenu;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;
using UnityEngine.XR;

[assembly: MelonInfo(typeof(UniversalManualAction.Core), "Universal Manual Action", "1.0.0", "OpenAI")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace UniversalManualAction
{
    public sealed class Core : MelonMod
    {
        private const string ModName = "Universal Manual Action";

        private MelonPreferences_Category _prefs;
        private MelonPreferences_Entry<bool> _enabled;
        private MelonPreferences_Entry<float> _pullDistance;
        private MelonPreferences_Entry<float> _returnDistance;
        private MelonPreferences_Entry<float> _cooldown;
        private MelonPreferences_Entry<bool> _requireGrip;

        private readonly Dictionary<int, HandState> _states = new Dictionary<int, HandState>();

        private sealed class HandState
        {
            public int GunId;
            public Vector3 LastPosition;
            public Vector3 GunAxis;
            public bool Initialized;
            public bool Pulled;
            public float LastCycleTime = -100f;
        }

        public override void OnInitializeMelon()
        {
            SetupPreferences();
            SetupMenu();
            MelonLogger.Msg(ModName + " loaded.");
        }

        private void SetupPreferences()
        {
            _prefs = MelonPreferences.CreateCategory(ModName);
            _enabled = _prefs.CreateEntry("Enabled", true, "Enable universal one-handed manual cycling.");
            _pullDistance = _prefs.CreateEntry("Pull Distance", 0.075f, "Backward hand travel required to start a cycle.");
            _returnDistance = _prefs.CreateEntry("Return Distance", 0.035f, "Forward hand travel required to finish a cycle.");
            _cooldown = _prefs.CreateEntry("Cycle Cooldown", 0.10f, "Minimum time between completed cycles.");
            _requireGrip = _prefs.CreateEntry("Require Grip", true, "Only cycle while the gun is held with grip.");

            _prefs.SaveToFile(false);
            MelonPreferences.Save();
        }

        private void SetupMenu()
        {
            Page page = Page.Root.CreatePage(ModName, Color.cyan);

            page.CreateBool("Enabled", Color.green, _enabled.Value, value => { _enabled.Value = value; Save(); });
            page.CreateFloat("Pull Distance", Color.white, _pullDistance.Value, 0.02f, 0.01f, 0.20f, value => { _pullDistance.Value = value; Save(); });
            page.CreateFloat("Return Distance", Color.white, _returnDistance.Value, 0.01f, 0.005f, 0.10f, value => { _returnDistance.Value = value; Save(); });
            page.CreateFloat("Cycle Cooldown", Color.white, _cooldown.Value, 0.01f, 0f, 1f, value => { _cooldown.Value = value; Save(); });
            page.CreateBool("Require Grip", Color.white, _requireGrip.Value, value => { _requireGrip.Value = value; Save(); });
            page.CreateFunction("Reset Motion Tracking", Color.yellow, ResetTracking);
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

            try
            {
                ProcessHand(Player.LeftHand, 0);
                ProcessHand(Player.RightHand, 1);
            }
            catch
            {
                // Never allow a mod-side input/object mismatch to take down the game.
            }
        }

        private void ProcessHand(Il2CppSLZ.Marrow.Hand hand, int handId)
        {
            if (hand == null)
                return;

            Gun gun = null;
            try
            {
                gun = Player.GetComponentInHand<Gun>(hand);
            }
            catch
            {
                return;
            }

            if (gun == null)
            {
                _states.Remove(handId);
                return;
            }

            if (_requireGrip && !IsGripHeld(handId))
            {
                ResetState(handId, hand.transform.position);
                return;
            }

            int key = handId;
            HandState state;
            if (!_states.TryGetValue(key, out state) || state.GunId != gun.GetInstanceID())
            {
                state = new HandState { GunId = gun.GetInstanceID() };
                _states[key] = state;
            }

            Vector3 handPosition = hand.transform.position;

            if (!state.Initialized)
            {
                state.Initialized = true;
                state.LastPosition = handPosition;
                state.GunAxis = GetGunAxis(gun);
                return;
            }

            Vector3 delta = handPosition - state.LastPosition;
            state.LastPosition = handPosition;

            Vector3 axis = state.GunAxis;
            if (axis.sqrMagnitude < 0.001f)
            {
                axis = GetGunAxis(gun);
                state.GunAxis = axis;
            }

            float signedTravel = Vector3.Dot(delta, axis);

            // BONELAB's native shotgun action is represented by the Gun slide methods.
            // We emulate the physical pull/return sequence for any Gun component,
            // including guns supplied by mods, without changing RPM or ammunition.
            if (!state.Pulled && signedTravel <= -_pullDistance.Value)
            {
                state.Pulled = true;
                TryCompleteSlidePull(gun);
            }
            else if (state.Pulled && signedTravel >= _returnDistance.Value)
            {
                if (Time.time - state.LastCycleTime >= _cooldown.Value)
                {
                    if (TryCompleteSlideReturn(gun))
                        state.LastCycleTime = Time.time;
                }
                state.Pulled = false;
            }
        }

        private Vector3 GetGunAxis(Gun gun)
        {
            try
            {
                // The local forward axis is the most consistent direction for
                // BONELAB's long guns and also works for custom weapon prefabs.
                return gun.transform.forward.normalized;
            }
            catch
            {
                return Vector3.forward;
            }
        }

        private bool TryCompleteSlidePull(Gun gun)
        {
            try
            {
                gun.CompleteSlidePull();
                return true;
            }
            catch
            {
                try
                {
                    gun.Charge();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private bool TryCompleteSlideReturn(Gun gun)
        {
            try
            {
                gun.CompleteSlideReturn();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsGripHeld(int handId)
        {
            try
            {
                XRNode node = handId == 0 ? XRNode.LeftHand : XRNode.RightHand;
                InputDevice device = InputDevices.GetDeviceAtXRNode(node);
                if (!device.isValid)
                    return true;

                bool grip;
                if (device.TryGetFeatureValue(CommonUsages.gripButton, out grip))
                    return grip;

                float axis;
                if (device.TryGetFeatureValue(CommonUsages.grip, out axis))
                    return axis > 0.5f;
            }
            catch
            {
            }

            return true;
        }

        private void ResetState(int handId, Vector3 position)
        {
            _states.Remove(handId);
        }

        private void ResetTracking()
        {
            _states.Clear();
            MelonLogger.Msg(ModName + ": motion tracking reset.");
        }
    }
}