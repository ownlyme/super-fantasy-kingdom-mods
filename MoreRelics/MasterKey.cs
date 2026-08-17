using System.Collections.Generic;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;
using SuperFantasyKingdom.Clickable;
using SuperFantasyKingdom.Spawner;

namespace MoreRelics
{
	// master key: remove relic requirements and grant 2 faith per day
	internal sealed class MasterKey : RelicEntry
	{
		private const string Id = "ownly_MasterKey";

		private const int FaithPerMorning = 2;

		private static readonly RelicDef Definition = new RelicDef
		{
			id = Id,
			cloneFrom = new[] { "GreenHouse", "BetterBerries", "Fruitarian", "SpawnTrees" },
			rarity = Rarity.Rare,
			cost = 6,
			title = "Master Key",
			description = "Relics ignore all their requirements.\n" + FaithPerMorning + "<sprite name=ResourceFaith> every morning."
		};

		public override RelicDef Def
		{
			get { return Definition; }
		}

		public override void OnMorning(int day)
		{
			// add to the tax summary
			if (TaxManager.Instance != null)
			{
				TaxManager.Instance.AddToStatistics(ResourceType.Faith, FaithPerMorning);
			}

			// spawn position
			BuildingCity source = null;
			if (CityManager.Instance != null)
			{
				source = CityManager.Instance.GetChurch();
				if (source == null)
				{
					source = CityManager.Instance.GetCastle();
				}
			}
			if (source == null || DroppedShardSpawner.Instance == null)
			{
				ResourceManager.Instance.AddResource(ResourceType.Faith, FaithPerMorning);
				return;
			}
			for (int i = 0; i < FaithPerMorning; i++)
			{
				DroppedShardSpawner.Instance.Spawn(source.GetPosition(), 1, ResourceType.Faith);
			}
		}

		internal static bool Active()
		{
			RelicEntry entry = Registry.Find(Id);
			return entry != null && entry.Held();
		}
	}

	// effects null-check and vanilla re-triggers on grid change
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
