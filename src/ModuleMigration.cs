using System;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
namespace PreLuna.BuildSupport
{
    // Merged into each plugin. Prepare runs before BasePlugin opens its GUID-based ConfigFile.
    public static class ModuleMigration
    {
        public static void Prepare(string oldGuid, string newGuid)
        {
            try { PrepareAt(Paths.ConfigPath, oldGuid, newGuid); }
            catch (Exception error) {
                BepInEx.Logging.Logger.CreateLogSource(newGuid + ".Migration").LogError("配置／保存记录迁移失败，未覆盖旧文件，停止本插件初始化：" + error);
                throw;
            }
        }
        public static void Guard(string oldGuid)
        {
            var loader = IL2CPPChainloader.Instance;
            if (loader != null && loader.Plugins.ContainsKey(oldGuid))
                throw new InvalidOperationException("PreLuna版本与旧插件 " + oldGuid + " 不能同时启用；旧配置可保留用于迁移和回退。");
        }
        public static void PrepareAt(string directory, string oldGuid, string newGuid)
        {
            if (newGuid == null || !newGuid.StartsWith("PreLuna.", StringComparison.Ordinal)
                || Path.GetFileName(newGuid) != newGuid || newGuid.IndexOf('/') >= 0 || newGuid.IndexOf('\\') >= 0 || newGuid.IndexOf(':') >= 0)
                throw new ArgumentException("PreLuna插件标识无效");
            string source = oldGuid + ".cfg", target = newGuid + ".cfg", fallback = null;
            if (oldGuid == "wanglocal.coopbotscustom") fallback = "BetterBots.cfg";
            else if (oldGuid == "wanglocal.prisonermovementcustom") fallback = "Localia.APEX_Prisoners.cfg";
            else if (oldGuid == "wanglocal.flashlightcustom") fallback = "FlashlightOverhaul.cfg";
            else if (oldGuid == "wanglocal.bioscannercustom") fallback = "INF.PersistentBioScanner.cfg";
            else if (oldGuid == "wanglocal.foamguncustom") fallback = "local.betterfoamgun.cfg";
            else if (oldGuid == "wanglocal.captain.boosters") {
                CopyValidated(directory, "wanglocal.captain.boosters.json", "PreLuna.Captain.Boosters.json", null, true);
                return; // The module's settings still share the Captain Config instance.
            }
            else if (oldGuid == "wanglocal.captain.commands" || oldGuid == "wanglocal.captain.fixes" || oldGuid == "WangLocal.MaxConsumables") return;
            else if (oldGuid != "wanglocal.prisonerhealthcustom" && oldGuid != "wanglocal.captain"
                && oldGuid != "wanglocal.headcamera" && oldGuid != "wanglocal.micromaster") throw new ArgumentException("未知旧插件标识");
            // Health deliberately does not fall back to the retired MindAttic.GTFO.EZ.cfg.
            CopyValidated(directory, source, target, fallback, false);
            // The existing independent FoamGun baseline seeds a fresh config with 0.4,
            // while Bind's 0.08 fallback still applies to an existing incomplete user file.
            if (oldGuid == "wanglocal.foamguncustom") CreateFoamDefaults(directory, target);
        }
        static void CreateFoamDefaults(string directory, string name)
        {
            string destination = Path.Combine(directory, name);
            if (File.Exists(destination)) return;
            Directory.CreateDirectory(directory);
            string temporary = destination + ".migrate-" + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, "## PreLuna FoamGunCustom 1.0.1\n[General]\nEnabled = true\n\n[Firing]\nShotInterval = 0.4\nLaunchForce = 32\nPelletSpread = 1\n\n[Ammo]\nCostPerPellet = 0.5\n", new UTF8Encoding(false));
                Validate(temporary, false);
                try { File.Move(temporary, destination, false); }
                catch (IOException) { if (!File.Exists(destination)) throw; Validate(destination, false); }
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        static void CopyValidated(string directory, string oldName, string newName, string fallback, bool json)
        {
            string destination = Path.Combine(directory, newName);
            if (File.Exists(destination)) { Validate(destination, json); return; }
            string source = Path.Combine(directory, oldName);
            if (!File.Exists(source) && fallback != null) source = Path.Combine(directory, fallback);
            if (!File.Exists(source)) return; // Normal first install: the plugin creates its own defaults.
            Validate(source, json);
            Directory.CreateDirectory(directory);
            string temporary = destination + ".migrate-" + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.Copy(source, temporary, false);
                Validate(temporary, json);
                try { File.Move(temporary, destination, false); }
                catch (IOException) { if (!File.Exists(destination)) throw; Validate(destination, json); }
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        static void Validate(string file, bool json)
        {
            if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("配置／保存记录超过合理大小：" + file);
            string text = File.ReadAllText(file, new UTF8Encoding(false, true));
            if (json) { ValidateSelections(text); return; }
            bool section = false;
            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null) {
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                if (line[0] == '[' && line.EndsWith("]", StringComparison.Ordinal) && line.Length > 2) { section = true; continue; }
                if (!section || line.IndexOf('=') <= 0 || line.IndexOf('\0') >= 0) throw new InvalidDataException("CFG结构无效，保留原文件：" + file);
            }
        }
        static void ValidateSelections(string text)
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("机器人选择记录必须是JSON对象");
            if (root.TryGetProperty("Version", out var version) && (!version.TryGetInt32(out int number) || number != 1))
                throw new InvalidDataException("机器人选择记录版本不支持");
            if (!root.TryGetProperty("Selections", out var selections)) return; // SelectionFile's existing empty default.
            if (selections.ValueKind != JsonValueKind.Array || selections.GetArrayLength() > 12) throw new InvalidDataException("机器人选择槽位表无效");
            int used = 0;
            foreach (var entry in selections.EnumerateArray()) {
                int slot = 0, category = 0;
                if (entry.ValueKind != JsonValueKind.Object
                    || entry.TryGetProperty("Slot", out var s) && !s.TryGetInt32(out slot)
                    || entry.TryGetProperty("Category", out var c) && !c.TryGetInt32(out category)
                    || slot < 0 || slot >= 4 || category < 0 || category >= 3
                    || !entry.TryGetProperty("Fingerprint", out var fingerprint) || fingerprint.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("机器人选择记录槽位／类别无效");
                string value = fingerprint.GetString();
                if (value == null || value.Length != 64) throw new InvalidDataException("强化剂指纹无效");
                for (int i = 0; i < value.Length; i++) if (!Uri.IsHexDigit(value[i])) throw new InvalidDataException("强化剂指纹无效");
                if (entry.TryGetProperty("SourceId", out var source) && !source.TryGetUInt32(out _)) throw new InvalidDataException("强化剂来源编号无效");
                int bit = 1 << (slot * 3 + category);
                if ((used & bit) != 0) throw new InvalidDataException("机器人选择记录有重复槽位");
                used |= bit;
            }
        }
    }
}
