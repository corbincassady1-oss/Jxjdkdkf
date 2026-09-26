using System;
using System.Diagnostics;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(healthregentoggle.HealthRegenToggle), "HealthRegenToggle", "3.2.0", "Jorink + customization")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace healthregentoggle
{
    public class HealthRegenToggle : MelonMod
    {
        private object _enableRegen, _enableVignette, _healingDelay, _regenSpeed, _healthRegen;
        private Type _pageType, _colorType;
        private object _page, _playerHealth;
        private MemberInfo _currHealth, _maxHealth, _alive, _regenRoutine, _vignette;
        private float _lastHealth = float.NaN;
        private double _nextHealAt, _lastTickTime;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private bool _menuReady;
        private bool _loggedRuntimeError;

        public override void OnInitializeMelon()
        {
            try
            {
                TrySetupBoneMenu();
                ResolveHealthMembers();
                MelonLogger.Msg("HealthRegenToggle 3.2.0 loaded.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("HealthRegenToggle initialization failed: " + ex);
            }
        }

        public override void OnUpdate()
        {
            try
            {
                if (!_menuReady)
                    TrySetupBoneMenu();

                if (_playerHealth == null)
                    RefreshPlayerHealth();

                if (_playerHealth == null)
                    return;

                StopVanillaRegen();

                float current = GetFloatMember(_currHealth, float.NaN);
                float max = GetFloatMember(_maxHealth, float.NaN);
                bool alive = GetBoolMember(_alive, true);

                if (float.IsNaN(current) || float.IsNaN(max) || !alive)
                {
                    _lastHealth = current;
                    return;
                }

                if (float.IsNaN(_lastHealth))
                {
                    _lastHealth = current;
                    _nextHealAt = _clock.Elapsed.TotalSeconds + GetSetting(_healingDelay, 2f);
                    _lastTickTime = _clock.Elapsed.TotalSeconds;
                    return;
                }

                if (current < _lastHealth - 0.0001f)
                {
                    _nextHealAt = _clock.Elapsed.TotalSeconds + GetSetting(_healingDelay, 2f);
                    _lastHealth = current;
                    _lastTickTime = _clock.Elapsed.TotalSeconds;
                    return;
                }

                _lastHealth = current;

                if (!GetBoolSetting(_enableRegen, true) || current >= max)
                    return;

                if (_clock.Elapsed.TotalSeconds < _nextHealAt)
                    return;

                double now = _clock.Elapsed.TotalSeconds;
                double previous = _lastTickTime <= 0d ? now : _lastTickTime;
                _lastTickTime = now;

                float speed = Math.Max(0f, GetSetting(_regenSpeed, 1f));
                float amount = Math.Max(0f, GetSetting(_healthRegen, 5f));
                float delta = (float)Math.Min(0.1d, Math.Max(0d, now - previous));
                float healed = current + amount * speed * delta;

                SetFloatMember(_currHealth, Math.Min(max, healed));
            }
            catch (Exception ex)
            {
                if (!_loggedRuntimeError)
                {
                    _loggedRuntimeError = true;
                    MelonLogger.Error("HealthRegenToggle runtime error: " + ex);
                }
            }
        }

        private void TrySetupBoneMenu()
        {
            if (_menuReady)
                return;

            Assembly boneLib = FindAssembly("BoneLib");
            if (boneLib == null)
                return;

            _pageType = boneLib.GetType("BoneLib.BoneMenu.Page", false);
            if (_pageType == null)
                return;

            Assembly unity = FindAssembly("UnityEngine.CoreModule");
            _colorType = unity?.GetType("UnityEngine.Color", false)
                         ?? Type.GetType("UnityEngine.Color, UnityEngine.CoreModule");
            if (_colorType == null)
                return;

            PropertyInfo rootProperty = _pageType.GetProperty(
                "Root",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            object root = rootProperty?.GetValue(null);
            if (root == null)
                return; // BoneLib has not initialized its menu yet.

            MethodInfo createPage = FindMethod(_pageType, "CreatePage", 4, true);
            _page = createPage.Invoke(root, new object[]
            {
                "HealthRegenToggle",
                MakeColor(0f, 1f, 0f, 1f),
                8,
                true
            });

            MethodInfo createBool = FindMethod(_pageType, "CreateBool", 4, false);
            MethodInfo createFloat = FindMethod(_pageType, "CreateFloat", 7, false);

            Action<bool> regenCallback = value => { };
            Action<bool> vignetteCallback = value => { };
            Action<float> delayCallback = value => { _nextHealAt = _clock.Elapsed.TotalSeconds + Math.Max(0f, value); };
            Action<float> speedCallback = value => { };
            Action<float> amountCallback = value => { };

            _enableRegen = createBool.Invoke(_page, new object[]
            {
                "Enable Regeneration",
                MakeColor(1f, 1f, 0f, 1f),
                true,
                regenCallback
            });

            _enableVignette = createBool.Invoke(_page, new object[]
            {
                "Enable Damage Vignette",
                MakeColor(1f, 1f, 0f, 1f),
                true,
                vignetteCallback
            });

            _healingDelay = createFloat.Invoke(_page, new object[]
            {
                "Healing Delay",
                MakeColor(0f, 1f, 1f, 1f),
                2f,
                0.1f,
                0f,
                30f,
                delayCallback
            });

            _regenSpeed = createFloat.Invoke(_page, new object[]
            {
                "Regen Speed",
                MakeColor(0f, 1f, 0f, 1f),
                1f,
                0.1f,
                0.1f,
                20f,
                speedCallback
            });

            _healthRegen = createFloat.Invoke(_page, new object[]
            {
                "Health Regen",
                MakeColor(0f, 1f, 0f, 1f),
                5f,
                0.5f,
                0f,
                100f,
                amountCallback
            });

            _menuReady = true;
            MelonLogger.Msg("HealthRegenToggle: BoneLib menu controls created.");
        }

        private void ResolveHealthMembers()
        {
            if (_playerHealth == null)
                RefreshPlayerHealth();

            if (_playerHealth == null)
                return;

            Type t = _playerHealth.GetType();
            _currHealth ??= FindMember(t, "curr_Health");
            _maxHealth ??= FindMember(t, "max_Health");
            _alive ??= FindMember(t, "alive");
            _regenRoutine ??= FindMember(t, "regenRoutine");
            _vignette ??= FindMember(t, "vigRend");
        }

        private void RefreshPlayerHealth()
        {
            try
            {
                Assembly jlibAssembly = FindAssembly("JLib");
                Type jlibType = jlibAssembly?.GetType("jlib.JLib", false);
                if (jlibType == null)
                    return;

                PropertyInfo p = jlibType.GetProperty(
                    "playerHealth",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (p != null)
                {
                    _playerHealth = p.GetValue(null);
                    if (_playerHealth != null)
                    {
                        ResolveHealthMembers();
                        return;
                    }
                }

                FieldInfo f = jlibType.GetField(
                    "playerHealth",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (f != null)
                {
                    _playerHealth = f.GetValue(null);
                    if (_playerHealth != null)
                        ResolveHealthMembers();
                }
            }
            catch
            {
                _playerHealth = null;
            }
        }

        private void StopVanillaRegen()
        {
            try
            {
                if (_regenRoutine == null || _playerHealth == null)
                    return;

                object routine = GetMember(_regenRoutine, _playerHealth);
                if (routine == null)
                    return;

                foreach (MethodInfo m in _playerHealth.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "StopCoroutine" || m.GetParameters().Length != 1)
                        continue;

                    try { m.Invoke(_playerHealth, new[] { routine }); }
                    catch { }

                    break;
                }
            }
            catch { }
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount, bool allowStatic)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.Instance | (allowStatic ? BindingFlags.Static : 0);

            foreach (MethodInfo m in type.GetMethods(flags))
            {
                if (m.Name == name && m.GetParameters().Length == parameterCount)
                    return m;
            }

            throw new MissingMethodException(type.FullName, name);
        }

        private static MemberInfo FindMember(Type type, string name)
        {
            return (MemberInfo)type.GetField(
                       name,
                       BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                   ?? type.GetProperty(
                       name,
                       BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static object GetMember(MemberInfo m, object target)
        {
            if (m is FieldInfo f) return f.GetValue(target);
            if (m is PropertyInfo p) return p.GetValue(target);
            return null;
        }

        private static void SetMember(MemberInfo m, object target, object value)
        {
            if (m is FieldInfo f) f.SetValue(target, value);
            else if (m is PropertyInfo p && p.CanWrite) p.SetValue(target, value);
        }

        private float GetFloatMember(MemberInfo m, float fallback)
        {
            if (m == null || _playerHealth == null)
                return fallback;

            try
            {
                object v = GetMember(m, _playerHealth);
                return v == null ? fallback : Convert.ToSingle(v);
            }
            catch { return fallback; }
        }

        private bool GetBoolMember(MemberInfo m, bool fallback)
        {
            if (m == null || _playerHealth == null)
                return fallback;

            try
            {
                object v = GetMember(m, _playerHealth);
                return v == null ? fallback : Convert.ToBoolean(v);
            }
            catch { return fallback; }
        }

        private void SetFloatMember(MemberInfo m, float value)
        {
            if (m == null || _playerHealth == null)
                return;

            try { SetMember(m, _playerHealth, value); }
            catch { }
        }

        private static float GetSetting(object entry, float fallback)
        {
            try
            {
                PropertyInfo p = entry?.GetType().GetProperty(
                    "Value",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                return p == null ? fallback : Convert.ToSingle(p.GetValue(entry));
            }
            catch { return fallback; }
        }

        private static bool GetBoolSetting(object entry, bool fallback)
        {
            try
            {
                PropertyInfo p = entry?.GetType().GetProperty(
                    "Value",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                return p == null ? fallback : Convert.ToBoolean(p.GetValue(entry));
            }
            catch { return fallback; }
        }

        private object MakeColor(float r, float g, float b, float a)
        {
            ConstructorInfo ctor = _colorType.GetConstructor(
                new[] { typeof(float), typeof(float), typeof(float), typeof(float) });

            if (ctor != null)
                return ctor.Invoke(new object[] { r, g, b, a });

            object color = Activator.CreateInstance(_colorType);
            _colorType.GetField("r")?.SetValue(color, r);
            _colorType.GetField("g")?.SetValue(color, g);
            _colorType.GetField("b")?.SetValue(color, b);
            _colorType.GetField("a")?.SetValue(color, a);
            return color;
        }

        private static Assembly FindAssembly(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                    return a;
            }

            return null;
        }
    }
}
