using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using MelonLoader;

[assembly: MelonInfo(typeof(AvaLoader.AvaLoaderMod), "AvaLoader", "1.0.0", "OpenAI")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace AvaLoader
{
    public sealed class AvaLoaderMod : MelonMod
    {
        private const string FolderName = "AvaLoader Avatar Folder";
        private object _rootPage;
        private object _avatarPage;
        private bool _menuBuilt;

        private string BasePath
        {
            get
            {
                var appType = FindType("UnityEngine.Application");
                var prop = appType?.GetProperty("persistentDataPath", BindingFlags.Public | BindingFlags.Static);
                var value = prop?.GetValue(null) as string;
                return string.IsNullOrEmpty(value) ? AppDomain.CurrentDomain.BaseDirectory : value;
            }
        }

        private string AvatarSourcePath => Path.Combine(BasePath, FolderName);
        private string ModsPath => Path.Combine(BasePath, "Mods");

        public override void OnInitializeMelon()
        {
            Directory.CreateDirectory(AvatarSourcePath);
            Directory.CreateDirectory(ModsPath);
            MelonLogger.Msg("[AvaLoader] Loaded.");
            MelonLogger.Msg("[AvaLoader] Avatar folder: " + AvatarSourcePath);
        }

        public override void OnUpdate()
        {
            if (_menuBuilt)
                return;

            try
            {
                if (FindType("BoneLib.BoneMenu.Page") != null)
                {
                    BuildMenu();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[AvaLoader] Menu setup waiting: " + ex.Message);
            }
        }

        private void BuildMenu()
        {
            var pageType = FindType("BoneLib.BoneMenu.Page");
            if (pageType == null)
                return;

            var rootProp = pageType.GetProperty("Root", BindingFlags.Public | BindingFlags.Static);
            _rootPage = rootProp?.GetValue(null);
            if (_rootPage == null)
                return;

            var colorType = FindType("UnityEngine.Color");
            var color = colorType == null ? null : Activator.CreateInstance(
                colorType, new object[] { 0.25f, 0.75f, 1f, 1f });

            var createPage = pageType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "CreatePage" && m.GetParameters().Length == 4);
            if (createPage == null)
                return;

            _avatarPage = createPage.Invoke(_rootPage, new object[] { "AvaLoader", color, 0, true });
            _menuBuilt = _avatarPage != null;

            if (_menuBuilt)
                RebuildMenu();

            MelonLogger.Msg("[AvaLoader] BoneMenu ready.");
        }

        private void RebuildMenu()
        {
            if (_avatarPage == null) return;
            try
            {
                var removeAll = _avatarPage.GetType().GetMethod("RemoveAll", Type.EmptyTypes);
                removeAll?.Invoke(_avatarPage, null);
            }
            catch { }

            AddFunction("Refresh Avatar List", RefreshAvatars);

            var folders = Directory.Exists(AvatarSourcePath)
                ? Directory.GetDirectories(AvatarSourcePath).Where(IsPackedAvatarFolder).OrderBy(Path.GetFileName).ToArray()
                : Array.Empty<string>();

            if (folders.Length == 0)
            {
                AddFunction("No packed avatars found", () => { });
                AddFunction("Folder: " + FolderName, () => { });
                return;
            }

            foreach (var folder in folders)
            {
                var name = Path.GetFileName(folder);
                var captured = folder;
                AddFunction("Load: " + TrimName(name), () => LoadAvatarFolder(captured));
            }
        }

        private void AddFunction(string label, Action action)
        {
            try
            {
                var colorType = FindType("UnityEngine.Color");
                var color = colorType == null ? null : Activator.CreateInstance(colorType, new object[] { 0.25f, 0.75f, 1f, 1f });
                var method = _avatarPage.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "CreateFunction" && m.GetParameters().Length == 3);
                method?.Invoke(_avatarPage, new object[] { label, color, action });
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[AvaLoader] Could not create menu item '" + label + "': " + ex.Message);
            }
        }

        private void RefreshAvatars()
        {
            RebuildMenu();
            MelonLogger.Msg("[AvaLoader] Avatar list refreshed.");
        }

        private void LoadAvatarFolder(string source)
        {
            try
            {
                var name = Path.GetFileName(source);
                var destination = Path.Combine(ModsPath, name);
                CopyDirectory(source, destination);
                MelonLogger.Msg("[AvaLoader] Installed avatar package: " + name);

                var barcode = TryReadAvatarBarcode(source);
                if (!TryReloadWarehouse())
                {
                    MelonLogger.Warning("[AvaLoader] No runtime Asset Warehouse reload method was found. The package is installed; restart BONELAB to register it.");
                    return;
                }

                MelonLogger.Msg("[AvaLoader] Asset Warehouse reload requested.");
                if (!string.IsNullOrEmpty(barcode))
                    _ = SwapWhenAvailable(barcode);
                else
                    MelonLogger.Warning("[AvaLoader] Could not read an avatar barcode from the pallet JSON.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[AvaLoader] Failed to load avatar: " + ex);
            }
        }

        private bool TryReloadWarehouse()
        {
            try
            {
                var aw = FindType("Il2CppSLZ.Marrow.Warehouse.AssetWarehouse");
                if (aw == null) return false;
                var instanceProp = aw.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var instance = instanceProp?.GetValue(null);

                foreach (var m in aw.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (m.GetParameters().Length != 0) continue;
                    if (!Regex.IsMatch(m.Name, "^(Reload|Refresh|Rescan|LoadMods|LoadPallets|LoadAll|Initialize)$", RegexOptions.IgnoreCase)) continue;
                    try
                    {
                        m.Invoke(m.IsStatic ? null : instance, null);
                        return true;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[AvaLoader] Warehouse reload reflection failed: " + ex.Message);
            }
            return false;
        }

        private async Task SwapWhenAvailable(string barcode)
        {
            for (var i = 0; i < 30; i++)
            {
                if (TrySwapAvatar(barcode))
                {
                    MelonLogger.Msg("[AvaLoader] Avatar swap requested: " + barcode);
                    return;
                }
                await Task.Delay(500);
            }
            MelonLogger.Warning("[AvaLoader] Avatar was installed but the crate was not available for swapping. Restart BONELAB once, then use AvaLoader again.");
        }

        private bool TrySwapAvatar(string barcode)
        {
            try
            {
                var playerType = FindType("BoneLib.Player");
                var rigProp = playerType?.GetProperty("RigManager", BindingFlags.Public | BindingFlags.Static);
                var rig = rigProp?.GetValue(null);
                if (rig == null) return false;

                foreach (var method in rig.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name != "SwapAvatarCrate") continue;
                    var p = method.GetParameters();
                    if (p.Length != 3 || p[1].ParameterType != typeof(bool)) continue;

                    object barcodeArg;
                    if (p[0].ParameterType == typeof(string))
                        barcodeArg = barcode;
                    else
                    {
                        var ctor = p[0].ParameterType.GetConstructor(new[] { typeof(string) });
                        if (ctor == null) continue;
                        barcodeArg = ctor.Invoke(new object[] { barcode });
                    }

                    object callback;
                    if (p[2].ParameterType == typeof(Action<bool>))
                        callback = new Action<bool>(ok => MelonLogger.Msg("[AvaLoader] Swap result: " + ok));
                    else if (p[2].ParameterType == typeof(Action))
                        callback = new Action(() => { });
                    else
                        continue;

                    method.Invoke(rig, new object[] { barcodeArg, true, callback });
                    return true;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[AvaLoader] Swap failed: " + ex.Message);
            }
            return false;
        }

        private static bool IsPackedAvatarFolder(string folder)
        {
            try
            {
                return Directory.GetFiles(folder, "*.pallet.json", SearchOption.TopDirectoryOnly).Length > 0;
            }
            catch { return false; }
        }

        private static string TryReadAvatarBarcode(string folder)
        {
            try
            {
                var pallet = Directory.GetFiles(folder, "*.pallet.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (pallet == null) return null;
                var json = File.ReadAllText(pallet);
                var avatar = Regex.Match(json, @"""barcode""\s*:\s*""([^""]*\.Avatar\.[^""]+)""", RegexOptions.IgnoreCase);
                return avatar.Success ? avatar.Groups[1].Value : null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[AvaLoader] Could not parse pallet JSON: " + ex.Message);
                return null;
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(source))
            {
                var target = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, target, true);
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
            }
        }

        private static string TrimName(string name)
        {
            return name.Length <= 42 ? name : name.Substring(0, 39) + "...";
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = asm.GetType(fullName, false);
                    if (type != null)
                        return type;
                }
                catch { }
            }
            return null;
        }
    }
}
