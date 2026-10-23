using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PreLuna.Captain.Boosters
{
    public readonly struct EffectValue
    {
        public readonly uint Id;
        public readonly float Value;
        public EffectValue(uint id, float value) { Id = id; Value = value; }
    }

    // A detached snapshot. No original inventory object, Prepared flag, or backend transaction is kept.
    public sealed class BoosterChoice
    {
        public uint SourceId { get; }
        public uint TemplateId { get; }
        public uint PacketId { get; }
        public int Category { get; }
        public int Uses { get; }
        public string Fingerprint { get; }
        public string WithoutConditionsFingerprint { get; }
        private readonly EffectValue[] effects;
        private readonly uint[] conditions;
        public EffectValue[] CopyEffects() => (EffectValue[])effects.Clone();
        public uint[] CopyConditions() => (uint[])conditions.Clone();

        public BoosterChoice(uint sourceId, uint templateId, uint packetId, int category, int uses,
            EffectValue[] effects, uint[] conditions, int effectLimit, int conditionLimit)
        {
            if (category < 0 || category > 2 || templateId == 0 || packetId == 0 || uses <= 0)
                throw new ArgumentException("强化剂类别、模板或剩余次数无效。");
            if (effects == null || effects.Length == 0 || effects.Length > effectLimit || conditions == null || conditions.Length > conditionLimit)
                throw new ArgumentException("强化剂效果／条件数量超出原版同步容量。");
            if (effects.Any(x => x.Id == 0 || float.IsNaN(x.Value) || float.IsInfinity(x.Value)))
                throw new ArgumentException("强化剂存在无效效果或非有限数值。");
            SourceId = sourceId; TemplateId = templateId; PacketId = packetId; Category = category; Uses = uses;
            this.effects = (EffectValue[])effects.Clone(); this.conditions = (uint[])conditions.Clone();
            var key = new StringBuilder().Append(category).Append(':').Append(templateId).Append(':').Append(packetId);
            foreach (var effect in effects) key.Append('|').Append(effect.Id).Append('=').Append(effect.Value.ToString("R", CultureInfo.InvariantCulture));
            key.Append('/');
            WithoutConditionsFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())));
            foreach (uint condition in conditions) key.Append(condition).Append(',');
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())));
        }
    }

    public sealed class SavedSelection
    {
        public int Slot { get; set; }
        public int Category { get; set; }
        public uint SourceId { get; set; }
        public string Fingerprint { get; set; }
    }
    public sealed class SelectionFile
    {
        public int Version { get; set; } = 1;
        public List<SavedSelection> Selections { get; set; } = new();
    }

    public sealed class BoosterSelection
    {
        private readonly SavedSelection[,] profiles = new SavedSelection[4, 3];
        private readonly long[] owners = new long[4];
        private readonly BoosterChoice[][] frozen = new BoosterChoice[4][];
        public bool IsFrozen { get; private set; }
        public bool RestoreLegacyConditionIdentity { get; set; }

        public bool Bind(int slot, long owner, bool isBot)
        {
            if (slot < 0 || slot >= 4) return false;
            if (owner == 0) { owners[slot] = 0; if (!IsFrozen) frozen[slot] = null; return false; }
            // Frozen equipment belongs to this expedition's bot slot, while
            // authority belongs to the current network owner. Never re-read
            // host inventory to replace a snapshot when a bot rejoins.
            if (!IsFrozen && owners[slot] != owner) frozen[slot] = null;
            owners[slot] = isBot ? owner : 0;
            if (!IsFrozen && !isBot) frozen[slot] = null;
            return isBot;
        }
        public bool Owns(int slot, long owner) => slot >= 0 && slot < 4 && owner != 0 && owners[slot] == owner;
        public bool Choose(int slot, long owner, bool host, bool bot, bool lobby, BoosterChoice choice)
        {
            if (!host || !bot || !lobby || IsFrozen || !Owns(slot, owner) || choice == null) return false;
            profiles[slot, choice.Category] = new SavedSelection {
                Slot = slot, Category = choice.Category, SourceId = choice.SourceId, Fingerprint = choice.Fingerprint
            };
            return true;
        }
        public bool Unequip(int slot, long owner, bool host, bool bot, bool lobby, int category)
        {
            if (!host || !bot || !lobby || IsFrozen || !Owns(slot, owner) || category < 0 || category > 2) return false;
            profiles[slot, category] = null;
            return true;
        }
        public BoosterChoice Resolve(int slot, int category, IReadOnlyList<BoosterChoice> catalog)
        {
            if (slot < 0 || slot >= 4 || category < 0 || category > 2 || catalog == null) return null;
            var selection = profiles[slot, category];
            if (selection == null) return null;
            var matches = catalog.Where(x => x.Category == category && x.Fingerprint == selection.Fingerprint).ToArray();
            if (matches.Length == 0 && RestoreLegacyConditionIdentity)
            {
                // Only the same source ID, category, template, packet and exact
                // effects can recover an old profile whose conditions were erased.
                // Do not infer new equipment from an ambiguous/different source.
                var legacy = catalog.Where(x => x.Category == category && x.SourceId == selection.SourceId
                    && x.WithoutConditionsFingerprint == selection.Fingerprint).ToArray();
                if (legacy.Length == 1) return legacy[0];
            }
            // IDs may be rebuilt by CustomBooster. A unique identical effect snapshot can still resolve.
            var exact = matches.FirstOrDefault(x => x.SourceId == selection.SourceId);
            return exact ?? (matches.Length == 1 ? matches[0] : null);
        }
        public BoosterChoice[] Get(int slot, long owner, IReadOnlyList<BoosterChoice> catalog)
        {
            if (!Owns(slot, owner)) return new BoosterChoice[3];
            if (IsFrozen) return frozen[slot] == null ? new BoosterChoice[3] : (BoosterChoice[])frozen[slot].Clone();
            return Enumerable.Range(0, 3).Select(x => Resolve(slot, x, catalog)).ToArray();
        }
        public void Freeze(IReadOnlyList<BoosterChoice> catalog)
        {
            if (IsFrozen) return;
            for (int slot = 0; slot < 4; slot++)
                frozen[slot] = owners[slot] == 0 ? null : Enumerable.Range(0, 3).Select(c => Resolve(slot, c, catalog)).ToArray();
            IsFrozen = true;
        }
        public void Thaw() { IsFrozen = false; Array.Clear(frozen, 0, frozen.Length); }
        public void ForgetBindings() { Thaw(); Array.Clear(owners, 0, owners.Length); }
        public SelectionFile Export()
        {
            var result = new SelectionFile();
            foreach (var selection in profiles)
                if (selection != null) result.Selections.Add(new SavedSelection {
                    Slot = selection.Slot, Category = selection.Category, SourceId = selection.SourceId, Fingerprint = selection.Fingerprint
                });
            return result;
        }
        public void Import(SelectionFile saved)
        {
            if (saved == null || saved.Version != 1 || saved.Selections == null || saved.Selections.Count > 12)
                throw new ArgumentException("不支持的机器人强化剂配置格式。");
            var next = new SavedSelection[4, 3];
            foreach (var entry in saved.Selections)
            {
                if (entry == null || entry.Slot < 0 || entry.Slot >= 4 || entry.Category < 0 || entry.Category > 2
                    || entry.Fingerprint == null || entry.Fingerprint.Length != 64 || !entry.Fingerprint.All(Uri.IsHexDigit)
                    || next[entry.Slot, entry.Category] != null)
                    throw new ArgumentException("机器人强化剂配置存在无效或重复槽位。");
                next[entry.Slot, entry.Category] = new SavedSelection {
                    Slot = entry.Slot, Category = entry.Category, SourceId = entry.SourceId, Fingerprint = entry.Fingerprint
                };
            }
            Array.Copy(next, profiles, next.Length);
            ForgetBindings();
        }
    }
}
