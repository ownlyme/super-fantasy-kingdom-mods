using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;

namespace MoreRelics
{
	// ------------------------------ millstone ------------------------------
	internal sealed class Millstone : RelicEntry
	{
		private static readonly RelicDef Definition = new RelicDef
		{
			id = "ownly_Millstone",
			cloneFrom = new[] { "GreenHouse", "BetterBerries", "Fruitarian", "SpawnTrees" },
			rarity = Rarity.Rare,
			cost = 7,
			title = "Millstone",
			description = "The bakery grinds its own wheat. Bread no longer needs flour."
		};

		public override RelicDef Def
		{
			get { return Definition; }
		}

		// BakeryUsesWheat already does this run-wide
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

			// m_Storage is re-derived from this list on every castle upgrade
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

			// the job supplier copied the ingredient type by value
			crafting.SelectRecipe(Traverse.Create(crafting).Field("m_SelectedRecipe").GetValue<int>());
			if (store != null)
			{
				// carried over, so the castle multiplier rides along
				store.SetStorageSpace(ResourceType.Wheat, flourSpace);
				store.SetStorageSpace(ResourceType.Flour, 0);
			}
		}
	}

	// a bakery built while the relic is held never sees Convert
	[HarmonyPatch(typeof(BuildingResource), "Init")]
	internal static class Patch_Millstone
	{
		private static void Prefix(BuildingResource __instance)
		{
			try
			{
				RelicEntry entry = Registry.Find("ownly_Millstone");
				if (entry != null && entry.Held())
				{
					Millstone.Rewrite(__instance);
				}
			}
			catch (System.Exception e)
			{
				Plugin.Log?.LogError("millstone failed on building init: " + e);
			}
		}
	}
}
