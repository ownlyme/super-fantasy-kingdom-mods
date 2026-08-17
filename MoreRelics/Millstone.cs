using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;

namespace MoreRelics
{
	// millstone: bakery uses wheat instead of flour and +10% duration (not mentioned)
	internal sealed class Millstone : RelicEntry
	{
		internal const float ExtraDuration = 0.1f;

		private static readonly RelicDef Definition = new RelicDef
		{
			id = "ownly_Millstone",
			cloneFrom = new[] { "GreenHouse", "BetterBerries", "Fruitarian", "SpawnTrees" },
			rarity = Rarity.Epic,
			cost = 10,
			title = "Millstone",
			description = "<sprite name=ResourceWheat> to <sprite name=ResourceBread> in Bakery."
		};

		public override RelicDef Def
		{
			get { return Definition; }
		}

		// BakeryUsesWheat mod installed
		public override bool Available
		{
			get { return !Chainloader.PluginInfos.ContainsKey("ownly.bakeryuseswheat"); }
		}

		public override void OnAcquired()
		{
			Convert();
		}

		// a save can restore buildings before relics
		public override void OnMorning(int day)
		{
			Convert();
		}

		internal static bool Rewrite(BuildingResource building)
		{
			if (building.GetBuildingType() != BuildingType.Bakery)
			{
				return false;
			}

			bool changed = false;
			if (building is BuildingCrafting crafting)
			{
				List<CraftingRecipe> recipes = crafting.GetCraftingRecipes();
				if (recipes != null)
				{
					for (int i = 0; i < recipes.Count; i++)
					{
						ResourceAmount[] ingredients = recipes[i].ingredients;
						if (ingredients == null)
						{
							continue;
						}
						for (int j = 0; j < ingredients.Length; j++)
						{
							if (ingredients[j].type == ResourceType.Flour)
							{
								ingredients[j].type = ResourceType.Wheat;
								changed = true;
							}
						}
					}
				}
			}

			ResourceStorage store = building.GetStorage();
			if (store == null)
			{
				return changed;
			}

			// Init creates m_Storage from this list, so patching it before
			List<ResourceAmount> slots = Traverse.Create(store).Field("storage").GetValue<List<ResourceAmount>>();
			if (slots == null)
			{
				return changed;
			}
			for (int i = 0; i < slots.Count; i++)
			{
				if (slots[i].type == ResourceType.Flour)
				{
					slots[i] = new ResourceAmount(ResourceType.Wheat, slots[i].amount);
					changed = true;
				}
			}
			return changed;
		}

		private void Convert()
		{
			if (CityManager.Instance == null || !(CityManager.Instance.GetBuilding(BuildingType.Bakery) is BuildingCrafting crafting))
			{
				return;
			}

			ResourceStorage store = crafting.GetStorage();
			int flourSpace = ((store != null) ? store.GetStorageSpace(ResourceType.Flour) : 0);
			if (!Rewrite(crafting))
			{
				return;
			}

			crafting.SelectRecipe(Traverse.Create(crafting).Field("m_SelectedRecipe").GetValue<int>());

			// Init already created m_Storage and bakeries have scaleWithBase false
			if (store != null)
			{
				store.SetStorageSpace(ResourceType.Wheat, flourSpace);
				store.SetStorageSpace(ResourceType.Flour, 0);
			}
			float before = crafting.GetTimer();
			crafting.SetTimer(before * (1f + ExtraDuration));
		}
	}

	// CityManager.Build calls Init on built and on save restore
	[HarmonyPatch(typeof(BuildingResource), "Init")]
	internal static class Patch_Millstone
	{
		private static void Prefix(BuildingResource __instance, out bool __state)
		{
			__state = false;
			try
			{
				RelicEntry entry = Registry.Find("ownly_Millstone");
				if (entry != null && entry.Held())
				{
					__state = Millstone.Rewrite(__instance);
				}
			}
			catch (System.Exception e)
			{
				Plugin.Log?.LogError("millstone failed on building init: " + e);
			}
		}

		// Postfix because m_Duration is assigned inside Init
		private static void Postfix(BuildingResource __instance, bool __state)
		{
			if (!__state)
			{
				return;
			}
			try
			{
				float before = __instance.GetTimer();
				__instance.SetTimer(before * (1f + Millstone.ExtraDuration));
			}
			catch (System.Exception e)
			{
				Plugin.Log?.LogError("millstone failed on building init: " + e);
			}
		}
	}
}
