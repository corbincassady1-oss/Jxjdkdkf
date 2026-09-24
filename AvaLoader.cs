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

                // Only do filesystem work here. Do not invoke AssetWarehouse or
                // avatar swapping from a BoneMenu callback; those reflection calls
                // can block the Unity main thread on some Quest builds.
                CopyDirectory(source, destination);

                MelonLogger.Msg("[AvaLoader] Installed avatar package: " + name);
                MelonLogger.Msg("[AvaLoader] Restart BONELAB once to register the new avatar.");

                try
                {
                    var barcode = TryReadAvatarBarcode(source);
                    if (!string.IsNullOrEmpty(barcode))
                        MelonLogger.Msg("[AvaLoader] Avatar barcode: " + barcode);
                }
                catch { }
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[AvaLoader] Failed to install avatar: " + ex);
            }
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
}
