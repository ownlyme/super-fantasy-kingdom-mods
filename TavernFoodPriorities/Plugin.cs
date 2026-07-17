using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Tavern;

namespace TavernFoodPriorities
{
	[BepInPlugin("ownly.tavernfoodpriorities", "Tavern Food Priorities", "1.6.0")]
	public class Plugin : BaseUnityPlugin
	{
		private void Awake()
		{
			new Harmony(Info.Metadata.GUID).PatchAll();
			Logger.LogInfo("Tavern Food Priorities loaded: units eat the cheapest food first");
		}
	}

	// eat the most expendable food first so the cook keeps upgrading meat.
	// gourmet is terminal so it goes early next to bread, and Cook() caps the
	// cooked->gourmet upgrade at 16-gourmet, so draining gourmet keeps it flowing.
	// fish depends on the FishFilet relic: with it Cook() turns fish into raw, so
	// fish sits just ahead of raw, without it fish is a dead end spent before berries.
	[HarmonyPatch(typeof(TavernFoodManager), "FindBestFood")]
	internal static class Patch_FindLowestFood
	{
		// relic: fish and raw tie on value but raw upgrades first, so fish goes ahead of raw
		private static readonly ResourceType[] eatOrderConserveFish =
		{
			ResourceType.Bread,
			ResourceType.Gourmet,
			ResourceType.Berry,
			ResourceType.Cooked,
			ResourceType.Fish,
			ResourceType.Raw,
		};

		// no relic: fish is a dead end, spend it before berries
		private static readonly ResourceType[] eatOrderSpendFish =
		{
			ResourceType.Bread,
			ResourceType.Gourmet,
			ResourceType.Fish,
			ResourceType.Berry,
			ResourceType.Cooked,
			ResourceType.Raw,
		};

		private static bool Prefix(TavernFoodManager __instance, ref ResourceType __result)
		{
			List<ResourceAmount> inventory = Traverse.Create(__instance).Field("m_Inventory").GetValue<List<ResourceAmount>>();

			bool cookFish = Traverse.Create(__instance).Field("m_CookFish").GetValue<bool>();
			ResourceType[] eatOrder = cookFish ? eatOrderConserveFish : eatOrderSpendFish;

			ResourceType result = ResourceType.None;
			int bestRank = int.MaxValue;

			if (inventory != null)
			{
				foreach (ResourceAmount item in inventory)
				{
					if (item.amount <= 0)
						continue;

					int rank = Array.IndexOf(eatOrder, item.type);
					if (rank < 0)
						continue;

					if (rank < bestRank)
					{
						bestRank = rank;
						result = item.type;
					}
				}
			}

			__result = result;
			return false;
		}
	}
}
