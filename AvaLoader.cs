using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Text;
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
            if (_avatarPage == null)
                return;

            try
            {
                var removeAll = _avatarPage.GetType().GetMethod("RemoveAll", Type.EmptyTypes);
                removeAll?.Invoke(_avatarPage, null);
            }
            catch { }

            AddFunction("Refresh Avatar List", RefreshAvatars);

            var folders = Directory.Exists(AvatarSourcePath)
                ? Directory.GetDirectories(AvatarSourcePath).OrderBy(Path.GetFileName).ToArray()
                : Array.Empty<string>();

            if (folders.Length == 0)
            {
                AddFunction("No avatar folders found", () => { });
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
                var color = colorType == null ? null : Activator.CreateInstance(
                    colorType, new object[] { 0.25f, 0.75f, 1f, 1f });

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

                if (!TryReloadWarehouse())
                {
                    MelonLogger.Warning("[AvaLoader] Asset Warehouse reload was not available. Restart BONELAB if the avatar does not appear.");
                }
                else
                {
                    MelonLogger.Msg("[AvaLoader] Asset Warehouse reload requested.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[AvaLoader] Failed to load avatar: " + ex);
            }
        }

        private bool TryReloadWarehouse()
        {
            // Prefer the game's AssetWarehouse reload method if exposed.
            try
            {
                var aw = FindType("Il2CppSLZ.Marrow.Warehouse.AssetWarehouse");
                if (aw != null)
                {
                    var instanceProp = aw.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    var instance = instanceProp?.GetValue(null);
                    if (instance != null)
                    {
                        foreach (var m in aw.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                        {
                            if (!m.Name.IndexOf("Reload", StringComparison.OrdinalIgnoreCase).Equals(-1) &&
                                m.GetParameters().Length == 0)
                            {
                                try
                                {
                                    m.Invoke(m.IsStatic ? null : instance, null);
                                    return true;
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            // Fallback: BONELAB Developer Mode exposes aw.reload over its local WebSocket.
            _ = SendReloadCommand();
            return true;
        }

        private async Task SendReloadCommand()
        {
            try
            {
                using (var ws = new ClientWebSocket())
                {
                    await ws.ConnectAsync(new Uri("ws://127.0.0.1:50152/console"), default);
                    var bytes = Encoding.UTF8.GetBytes("aw.reload");
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, default);
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "AvaLoader", default);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[AvaLoader] Runtime reload unavailable: " + ex.Message);
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
