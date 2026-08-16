using HarmonyLib;
using SuperFantasyKingdom;

namespace MoreRelics
{
	// ------------------------------ phalanx ------------------------------
	internal sealed class Phalanx : RelicEntry
	{
		private const string Id = "ownly_Phalanx";

		// position packs both axes: lane = pos / 10, rank = pos % 10, higher rank is further forward
		private const int Lanes = 10;
		private const int MaxRank = 5;
		private const int LastFieldSpot = 45;

		private static readonly RelicDef Definition = new RelicDef
		{
			id = Id,
			cloneFrom = new[] { "GreenHouse", "BetterBerries", "Fruitarian", "SpawnTrees" },
			rarity = Rarity.Legendary,
			cost = 12,
			title = "Phalanx",
			description = "Only the first unit of each column can be hurt.",
			bossOnly = true
		};

		public override RelicDef Def
		{
			get { return Definition; }
		}

		internal static bool Covered(Entity entity)
		{
			UnitBase unit = entity as UnitBase;
			if (unit == null || GridManager.Instance == null)
			{
				return false;
			}
			int position = unit.GetGridPosition();
			if (position < 1 || position > LastFieldSpot)
			{
				return false;
			}
			int rank = position % Lanes;
			int lane = position / Lanes;
			if (rank < 1 || rank > MaxRank)
			{
				return false;
			}
			for (int ahead = rank + 1; ahead <= MaxRank; ahead++)
			{
				UnitBase front = GridManager.Instance.GetUnitByGridPosition(lane * Lanes + ahead);
				if (front != null && !front.IsDead() && !front.IsAway())
				{
					return true;
				}
			}
			return false;
		}
	}

	// ReceiveDirectDamage is ReceiveDamage(direct: true), so this one override is the whole path
	[HarmonyPatch(typeof(UnitHealth), "ReceiveDamage", new[] { typeof(Damage), typeof(bool), typeof(bool) })]
	internal static class Patch_Phalanx
	{
		// a blocked hit is inert on purpose, vanilla never runs
		// so no knockback, no GetAngryAmount() chip damage, no UnitBlock sound, no OnReceiveHit
		private static bool Prefix(UnitHealth __instance, ref float __result)
		{
			try
			{
				RelicEntry entry = Registry.Find("ownly_Phalanx");
				if (entry == null || !entry.Held() || !Phalanx.Covered(__instance.GetEntity()))
				{
					return true;
				}
				__result = 0f;
				return false;
			}
			catch
			{
				return true;
			}
		}
	}
}
