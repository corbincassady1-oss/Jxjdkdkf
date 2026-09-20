using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(FusionKick.Main), "FusionKick", "1.0.0", "OpenAI")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace FusionKick;

public sealed class Main : MelonMod
{
    private static readonly string MenuColorTypeName = "UnityEngine.Color";
    private static readonly object MenuLock = new();
    private static object _page;

    private static Assembly FusionAssembly =>
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "LabFusion", StringComparison.OrdinalIgnoreCase));

    public override void OnLateInitializeMelon()
    {
        try
        {
            BuildMenu();
            MelonLogger.Msg("FusionKick loaded.");
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"FusionKick initialization failed: {ex}");
        }
    }

    private static void BuildMenu()
    {
        lock (MenuLock)
        {
            var root = GetStaticProperty("BoneLib.BoneMenu.Page", "Root");
            if (root == null)
            {
                MelonLogger.Warning("BoneLib BoneMenu is not ready yet.");
                return;
            }

            if (_page == null)
                _page = CreatePage(root, "FusionKick");

            RefreshMenu();
        }
    }

    private static void RefreshMenu()
    {
        lock (MenuLock)
        {
            if (_page == null)
                return;

            InvokeInstance(_page, "RemoveAll");

            if (FusionAssembly == null)
            {
                AddFunction("Fusion not loaded", null);
                AddFunction("Refresh", RefreshMenu);
                return;
            }

            if (!IsFusionHost())
            {
                AddFunction("Host only", null);
                AddFunction("Refresh", RefreshMenu);
                return;
            }

            var players = GetPlayers();
            if (players.Count == 0)
            {
                AddFunction("No players found", null);
                AddFunction("Refresh", RefreshMenu);
                return;
            }

            foreach (var player in players)
            {
                var capturedPlayer = player;
                var name = GetPlayerName(player);
                AddFunction($"KICK: {name}", () => KickPlayer(capturedPlayer, name));
            }

            AddFunction("Refresh Player List", RefreshMenu);
        }
    }

    private static object CreatePage(object root, string name)
    {
        var pageType = root.GetType();
        var methods = pageType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => string.Equals(m.Name, "CreatePage", StringComparison.Ordinal))
            .OrderByDescending(m => m.GetParameters().Length);

        foreach (var method in methods)
        {
            var p = method.GetParameters();
            if (p.Length < 2)
                continue;

            var args = new object[p.Length];
            bool valid = true;

            for (int i = 0; i < p.Length; i++)
            {
                if (p[i].ParameterType == typeof(string))
                    args[i] = i == 0 ? name : null;
                else if (p[i].ParameterType.FullName == MenuColorTypeName)
                    args[i] = CreateUnityColor(1f, 0.25f, 0.15f, 1f);
                else if (p[i].ParameterType == typeof(int))
                    args[i] = 10;
                else if (p[i].HasDefaultValue)
                    args[i] = p[i].DefaultValue;
                else
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
                continue;

            try
            {
                return method.Invoke(root, args);
            }
            catch
            {
                // Try another overload.
            }
        }

        throw new MissingMethodException("BoneLib Page.CreatePage overload was not found.");
    }

    private static void AddFunction(string label, Action action)
    {
        AddFunction(label, action, false);
    }

    private static void AddFunction(string label, Action action, bool unused)
    {
        var pageType = _page.GetType();
        var methods = pageType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => string.Equals(m.Name, "CreateFunction", StringComparison.Ordinal));

        foreach (var method in methods)
        {
            var p = method.GetParameters();
            if (p.Length < 2 || p.Length > 4)
                continue;

            var args = new object[p.Length];
            bool valid = true;
            int stringIndex = 0;

            for (int i = 0; i < p.Length; i++)
            {
                var pt = p[i].ParameterType;

                if (pt == typeof(string))
                {
                    args[i] = stringIndex++ == 0 ? label : null;
                }
                else if (pt.FullName == MenuColorTypeName)
                {
                    args[i] = CreateUnityColor(1f, 0.25f, 0.15f, 1f);
                }
                else if (pt == typeof(Action))
                {
                    args[i] = action;
                }
                else if (p[i].HasDefaultValue)
                {
                    args[i] = p[i].DefaultValue;
                }
                else
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
                continue;

            try
            {
                method.Invoke(_page, args);
                return;
            }
            catch
            {
                // Try another overload.
            }
        }

        throw new MissingMethodException("BoneLib Page.CreateFunction overload was not found.");
    }

    private static object CreateUnityColor(float r, float g, float b, float a)
    {
        var colorType = FindLoadedType(MenuColorTypeName);
        if (colorType == null)
            throw new TypeLoadException("UnityEngine.Color is not loaded.");

        var ctor = colorType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
        if (ctor != null)
            return ctor.Invoke(new object[] { r, g, b, a });

        var value = Activator.CreateInstance(colorType);
        colorType.GetField("r")?.SetValue(value, r);
        colorType.GetField("g")?.SetValue(value, g);
        colorType.GetField("b")?.SetValue(value, b);
        colorType.GetField("a")?.SetValue(value, a);
        return value;
    }

    private static object GetStaticProperty(string typeName, string propertyName)
    {
        try
        {
            var type = FindLoadedType(typeName);
            return type?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    private static void InvokeInstance(object instance, string methodName)
    {
        instance.GetType().GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)?.Invoke(instance, null);
    }

    private static Type FindLoadedType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }
            catch
            {
            }
        }

        return null;
    }

    private static void KickPlayer(object player, string name)
    {
        try
        {
            var fusion = FusionAssembly;
            if (fusion == null || !IsFusionHost())
                return;

            var senderType = fusion.GetType("LabFusion.Senders.PermissionSender", throwOnError: true);
            var commandType = fusion.GetType("LabFusion.Senders.PermissionCommandType", throwOnError: true);

            var command = Enum.Parse(commandType, "KICK", ignoreCase: true);
            var method = senderType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, "SendPermissionRequest", StringComparison.Ordinal))
                        return false;

                    var p = m.GetParameters();
                    return p.Length == 2 &&
                           p[0].ParameterType == commandType &&
                           string.Equals(p[1].ParameterType.Name, "PlayerID", StringComparison.Ordinal);
                });

            if (method == null)
                throw new MissingMethodException("Fusion PermissionSender.SendPermissionRequest(KICK, PlayerID) was not found.");

            method.Invoke(null, new[] { command, player });
            MelonLogger.Msg($"Fusion kick request sent for {name}.");
            RefreshMenu();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Failed to kick {name}: {ex.GetBaseException().Message}");
        }
    }

    private static bool IsFusionHost()
    {
        try
        {
            var networkInfo = FusionAssembly?.GetType("LabFusion.Network.NetworkInfo", false);
            var property = networkInfo?.GetProperty("IsHost", BindingFlags.Public | BindingFlags.Static);
            return property?.GetValue(null) is bool b && b;
        }
        catch
        {
            return false;
        }
    }

    private static List<object> GetPlayers()
    {
        var result = new List<object>();

        try
        {
            var manager = FusionAssembly?.GetType("LabFusion.Player.PlayerIDManager", false);
            var property = manager?.GetProperty("PlayerIDs", BindingFlags.Public | BindingFlags.Static);
            if (property?.GetValue(null) is not IEnumerable list)
                return result;

            foreach (var player in list)
            {
                if (player == null)
                    continue;

                var isMeValue = player.GetType().GetProperty("IsMe")?.GetValue(player);
                if (isMeValue is bool isMe && isMe)
                    continue;

                result.Add(player);
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Failed to read Fusion players: {ex.GetBaseException().Message}");
        }

        return result;
    }

    private static string GetPlayerName(object player)
    {
        try
        {
            var metadata = player.GetType().GetProperty("Metadata")?.GetValue(player);
            var username = metadata?.GetType().GetProperty("Username")?.GetValue(metadata);
            var getName = username?.GetType().GetMethod("GetValueOrEmpty", Type.EmptyTypes);
            var result = getName?.Invoke(username, null) as string;

            if (!string.IsNullOrWhiteSpace(result))
                return result;
        }
        catch
        {
        }

        try
        {
            return $"Player {player.GetType().GetProperty("PlatformID")?.GetValue(player)}";
        }
        catch
        {
            return "Unknown Player";
        }
    }
}
