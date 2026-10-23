using System;

namespace PreLuna.CoopBotsCustom.Combat
{
    public struct GunFacts
    {
        public int Clip, Capacity, Reserve, Pellets, Pierce, Burst;
        public float Damage, FalloffStart, FalloffEnd, FalloffFloor, Spread, Cone, PelletSpread;
        public float Interval, BurstPause, Reload, Equip, Aim, Charge, Cooldown, Cost, Stagger;
    }
    public struct TargetFacts
    {
        public float Health, Distance, Radius, WeakDamage, StaggerRemaining, ApproachTime, ExtraHealth;
        public bool Flyer, Attacking, Charging, HighDamage, TargetsTeam, Immediate, Aligned;
    }
    public struct GunPlan
    {
        public bool Usable, ReadyKill, ReadyInterrupt, CanFinish;
        public int Shots, ReadyShots, Reloads;
        public float HitChance, PerShot, Time, Cost, Score, ReadyTime, ReliefTime;
    }
    public static class CombatPolicy
    {
        public static float Clamp(float v, float min, float max, float fallback)
        { return float.IsNaN(v) || float.IsInfinity(v) ? fallback : Math.Max(min, Math.Min(max, v)); }
        public static float Falloff(GunFacts g, float distance)
        {
            float start = Clamp(g.FalloffStart, 0, 10000, 0), end = Clamp(g.FalloffEnd, start, 10000, start);
            float floor = Clamp(g.FalloffFloor, 0.01f, 1, 0.1f);
            if (distance <= start) return 1;
            if (end <= start || distance >= end) return floor;
            return 1 - (1 - floor) * (distance - start) / (end - start);
        }
        public static GunPlan Evaluate(GunFacts g, TargetFacts t, bool current, bool reloading, float remaining)
        {
            var p = new GunPlan { Time = 100000, Cost = 100000, ReliefTime = 100000 };
            if (g.Capacity <= 0 || g.Damage <= 0 || !float.IsFinite(g.Damage) || g.Clip + g.Reserve <= 0) return p;
            float distance = Clamp(t.Distance, .5f, 100000, 50), radius = Clamp(t.Radius, .12f, 3f, .4f);
            float angular = (float)(Math.Atan(radius / distance) * 180 / Math.PI);
            float spread = Clamp(g.Spread, 0, 180, 2);
            // Conservative angular footprint estimate, not a prediction of each native ray.
            float aim = .85f * Math.Min(1, angular * angular / Math.Max(.01f, spread * spread));
            float pellets = 1;
            if (g.Pellets > 1)
            {
                float cone = Math.Max(.25f, g.Cone + g.PelletSpread * .5f + spread);
                pellets = g.Pellets * Math.Min(.65f, angular * angular / (cone * cone));
                aim = .85f;
            }
            if (t.Flyer) aim *= .8f;
            p.HitChance = Clamp(aim, .02f, .85f, .5f);
            float falloff = Falloff(g, distance);
            p.PerShot = g.Damage * falloff * Math.Max(.01f, pellets) * p.HitChance * Clamp(t.WeakDamage, .1f, 3, 1);
            float health = Clamp(t.Health, .1f, 1000000, 1000000);
            // Extra health is only immediate nearby threats, never the whole incoming wave.
            float demand = health * 1.15f + Clamp(t.ExtraHealth, 0, 1000000, 0) * .5f;
            p.Shots = (int)Math.Min(100000, Math.Max(1, Math.Ceiling(demand / Math.Max(.01f, p.PerShot))));
            p.ReadyShots = reloading ? 0 : Math.Max(0, g.Clip);
            p.ReadyKill = p.ReadyShots >= p.Shots;
            float bodyStagger = g.Damage * falloff * Math.Max(.01f, pellets) * p.HitChance * Math.Max(0, g.Stagger);
            int interruptShots = t.StaggerRemaining > 0 && bodyStagger > 0 ?
                (int)Math.Min(100000, Math.Max(1, Math.Ceiling(t.StaggerRemaining / bodyStagger))) : 100000;
            p.ReadyInterrupt = t.Immediate && interruptShots <= 2 && p.ReadyShots >= interruptShots;
            p.CanFinish = g.Clip + g.Reserve >= p.Shots;
            float interval = Clamp(g.Interval, .01f, 30, .2f);
            int refill = reloading ? Math.Min(g.Capacity, Math.Max(0, g.Clip) + Math.Max(0, g.Reserve)) :
                Math.Min(g.Capacity, Math.Max(0, g.Reserve));
            int afterReady = Math.Max(0, p.Shots - p.ReadyShots);
            p.Reloads = afterReady == 0 ? 0 : refill > 0 ? (int)Math.Ceiling((double)afterReady / refill) : 10000;
            float reload = Clamp(g.Reload, .05f, 60, 3);
            remaining = Clamp(remaining, .05f, 60, reload);
            float preparation = (current ? 0 : Clamp(g.Equip, .05f, 30, .5f)) +
                Clamp(g.Aim, 0, 10, .2f) + Clamp(g.Charge, 0, 30, 0);
            p.ReadyTime = preparation + (reloading ? Math.Max(.05f, remaining) : g.Clip <= 0 ? reload : 0);
            float waiting = p.Reloads * reload;
            if (reloading && p.Reloads > 0) waiting += Math.Max(.05f, remaining) - reload;
            float firing = Math.Max(0, p.Shots - 1) * interval;
            if (g.Burst > 1) firing += ((p.Shots - 1) / g.Burst) * Math.Max(0, g.BurstPause - interval);
            firing += Math.Max(0, p.Shots - 1) * Clamp(g.Cooldown, 0, 30, 0);
            p.Time = Math.Min(100000, preparation + waiting + firing);
            p.ReliefTime = p.CanFinish ? p.Time : 100000;
            if (t.Immediate && interruptShots <= 2 && g.Clip + g.Reserve >= interruptShots)
            {
                float interruptWait = p.ReadyShots >= interruptShots ? 0 :
                    reloading ? Math.Max(.05f, remaining) : reload;
                p.ReliefTime = Math.Min(p.ReliefTime, preparation + interruptWait +
                    Math.Max(0, interruptShots - 1) * (interval + Math.Max(0, g.Cooldown)));
            }
            p.Cost = p.Shots * Clamp(g.Cost, .01f, 1000, 1);
            // Penetration earns a small bonus only when another observed threat is aligned.
            float pierce = t.Aligned && g.Pierce > 1 ? 1.12f : 1f;
            p.Usable = p.PerShot >= .05f && p.HitChance >= .06f;
            float efficiency = t.Immediate || (t.Flyer && t.Attacking) ? .02f : .12f;
            p.Score = p.Usable ? pierce / (.25f + p.Time + p.Cost * efficiency) : 0;
            if (!p.CanFinish) p.Score *= .35f;
            // An ammo-efficient estimate with zero ready bullets must not win a panic swap.
            if (t.Immediate && !p.ReadyKill && !p.ReadyInterrupt) p.Score *= .65f;
            return p;
        }
        public static bool ShouldSwitch(GunPlan current, GunPlan other, bool empty, bool reloading,
            bool equipping, bool immediate, float held, float hold, float improvement)
        {
            if (equipping || !other.Usable || other.Score <= 0) return false;
            bool usefulNow = other.ReadyKill || other.ReadyInterrupt;
            if (reloading)
            {
                // Commitment to reload: urgency + useful ready magazine + clear total-time benefit.
                return immediate && usefulNow && other.ReliefTime + .2f < current.ReliefTime &&
                    (other.ReadyInterrupt || other.Time < current.Time * .8f);
            }
            if (!current.Usable || current.Score <= 0)
                return other.CanFinish || usefulNow || (empty && other.ReadyShots > 0);
            if (immediate && usefulNow && other.ReliefTime + .2f < current.ReliefTime) return true;
            if (empty)
            {
                // Never discard a suitable fast reload solely because equip time is shorter.
                if (!other.CanFinish && !usefulNow) return false;
                return other.Time + .2f < current.Time || (usefulNow && other.ReliefTime + .2f < current.ReliefTime);
            }
            if (held < hold) return false;
            if (!other.ReadyKill && !other.ReadyInterrupt && current.ReadyShots > 0)
            {
                // When neither magazine can finish a large enemy, allow a clearly stronger sustained route.
                if (current.ReadyKill || !other.CanFinish || other.ReadyShots < 2 ||
                    other.ReadyShots * other.PerShot < current.ReadyShots * current.PerShot * 1.3f ||
                    other.Time >= current.Time * .7f) return false;
            }
            return other.Score >= current.Score * (1 + improvement);
        }
        public static int Tier(TargetFacts t)
        {
            if (t.Immediate && t.TargetsTeam) return 5;
            if (t.HighDamage && t.ApproachTime < 3) return 4;
            if ((t.Flyer || t.Distance > 8) && (t.Attacking || t.Charging) && t.TargetsTeam) return 3;
            if (t.Distance < 12) return 2;
            return 1;
        }
        public static float TargetScore(TargetFacts t, int assigned, bool current, bool guard, float capability)
        {
            float score = Tier(t) * 1000 + 100 / (1 + t.Distance * .1f);
            if (current) score += 45;
            if (capability <= 0 && Tier(t) < 5) score = 75;
            // Soft allocation: never make the only attackable target unusable.
            if (Tier(t) < 5)
            {
                int desired = t.HighDamage || (t.Flyer && (t.Attacking || t.Charging)) ? 2 : 1;
                if (assigned >= desired) score -= 180;
                if (guard && t.Distance > 12) score -= 500;
            }
            return score;
        }
        public static bool CanTacticalReload(int clip, int capacity, int reserve, float fraction,
            bool busy, bool immediate, float quiet, float quietRequired, float arrival, float duration)
        {
            if (busy || immediate || capacity <= 0 || reserve <= 0 || clip <= 0 || clip >= capacity) return false;
            if ((float)clip / capacity > fraction || quiet < quietRequired) return false;
            return arrival > duration + .75f;
        }
        public static bool HoldTarget(bool valid, bool emergency, float elapsed, float hold,
            int currentTier, int bestTier, float currentScore, float bestScore)
        {
            if (!valid) return false;
            if (emergency && bestTier > currentTier) return false;
            return elapsed < hold || (bestTier == currentTier && bestScore < currentScore + 80);
        }
    }
}
