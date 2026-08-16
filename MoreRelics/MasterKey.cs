using System.Collections.Generic;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;
using SuperFantasyKingdom.Clickable;

namespace MoreRelics
{
	// ------------------------------ master key ------------------------------
	internal sealed class MasterKey : RelicEntry
	{
		private const string Id = "ownly_MasterKey";

		private static readonly RelicDef Definition = new RelicDef
		{
			id = Id,
			cloneFrom = new[] { "GreenHouse", "BetterBerries", "Fruitarian", "SpawnTrees" },
			rarity = Rarity.Epic,
			cost = 8,
			title = "Master Key",
			description = "Relics ignore their kingdom level, building and unit requirements."
		};

		public override RelicDef Def
		{
			get { return Definition; }
		}

		internal static bool Active()
		{
			RelicEntry entry = Registry.Find(Id);
			return entry != null && entry.Held();
		}
	}

	// a missing building is safe - the effects null-check and vanilla re-triggers on grid change
	[HarmonyPatch(typeof(Relic), "GetRequiredBuilding")]
	internal static class Patch_RelicBuilding
	{
		private static void Postfix(ref BuildingType __result)
		{
			try
			{
				if (MasterKey.Active())
				{
					__result = BuildingType.None;
				}
			}
			catch
			{
			}
		}
	}

	[HarmonyPatch(typeof(DropBossChest), "PreparePool")]
	internal static class Patch_BossChestPool
	{
		private static void Postfix(DropBossChest __instance)
		{
			try
			{
				if (!MasterKey.Active())
				{
					return;
				}
				List<string> pool = Traverse.Create(__instance).Field("RelicPool").GetValue<List<string>>();
				if (pool == null)
				{
					return;
				}
				foreach (KeyValuePair<string, Unit> unit in AddressablesManager.Instance.GetAllUnits())
				{
					List<string> related = ((unit.Value != null) ? unit.Value.GetRelatedRelics() : null);
					if (related == null)
					{
						continue;
					}
					foreach (string relic in related)
					{
						if (RelicManager.Instance.IsUnlocked(relic) && !RelicManager.Instance.HasRelic(relic) && !pool.Contains(relic))
						{
							pool.Add(relic);
						}
					}
				}
			}
			catch
			{
			}
		}
	}

	// nothing is held on the title screen, so its "requires level X" text stays honest
	[HarmonyPatch(typeof(Relic), "GetRequiredKingdomLevel")]
	internal static class Patch_RelicLevel
	{
		private static void Postfix(ref int __result)
		{
			try
			{
				if (MasterKey.Active())
				{
					__result = 0;
				}
			}
			catch
			{
			}
		}
	}
}
