using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;
using SuperFantasyKingdom.Clickable;
using UnityEngine;
using UnityEngine.UI;

namespace MoreRelics
{
	[BepInPlugin("ownly.morerelics", "More Relics", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		internal static ManualLogSource Log;

		private void Awake()
		{
			Log = Logger;
			try
			{
				new Harmony(Info.Metadata.GUID).PatchAll();
			}
			catch (System.Exception e)
			{
				// one bad target stops the iteration and every class after it never applies
				Log.LogError("PatchAll THREW, later patches did not apply: " + e);
			}
		}
	}

	// ------------------------------ the relic table ------------------------------
	internal static class Registry
	{
		public static readonly RelicEntry[] Entries =
		{
			new Cornucopia(),
			new Millstone(),
			new MasterKey(),
			new Phalanx(),
			new SousChef(),
			new Grimoire()
		};

		public static RelicEntry Find(string identifier)
		{
			foreach (RelicEntry entry in Entries)
			{
				if (entry.Def.id == identifier && entry.Available)
				{
					return entry;
				}
			}
			return null;
		}
	}

	// ids are prefixed - the addressables dictionary and the run save are both keyed by name
	internal sealed class RelicDef
	{
		public string id;
		public string[] cloneFrom;
		public Rarity rarity;
		public int cost;
		public string title;
		public string description;
		// no relic sets one today, and registration still writes it to clear the donor's own
		public int requiredKingdomLevel = 0;
		public bool bossOnly;
	}

	internal abstract class RelicEntry
	{
		public abstract RelicDef Def { get; }

		// false drops it before registration
		public virtual bool Available
		{
			get { return true; }
		}

		public virtual void OnMorning(int day)
		{
		}

		public virtual void OnAcquired()
		{
		}

		public bool Held()
		{
			return RelicManager.Instance != null && RelicManager.Instance.HasRelic(Def.id);
		}
	}

	// ------------------------------ registration ------------------------------
	// every relic consumer reads these two live collections, and it has to happen before a save loads
	[HarmonyPatch(typeof(AddressablesManager), "IsLoaded")]
	internal static class Patch_Register
	{
		private static bool s_Done;

		private static void Postfix(AddressablesManager __instance, bool __result)
		{
			if (!__result || s_Done)
			{
				return;
			}
			s_Done = true;
			try
			{
				Register(__instance);
			}
			catch (System.Exception e)
			{
				Plugin.Log?.LogError("relic registration failed: " + e);
			}
		}

		private static void Register(AddressablesManager addressables)
		{
			Dictionary<string, Relic> relics = addressables.GetAllRelics();
			List<string> identifiers = addressables.GetAllRelicIdentifiers();
			foreach (RelicEntry entry in Registry.Entries)
			{
				RelicDef def = entry.Def;
				if (!entry.Available)
				{
					Plugin.Log?.LogInfo("skipping " + def.id + ", another mod already covers it");
					continue;
				}
				if (relics.ContainsKey(def.id))
				{
					continue;
				}
				Relic donor = Donor(relics, def);
				if (donor == null)
				{
					Plugin.Log?.LogError("no donor prefab for " + def.id);
					continue;
				}
				Relic clone = Object.Instantiate(donor);
				Object.DontDestroyOnLoad(clone.gameObject);
				clone.gameObject.SetActive(false);
				clone.name = def.id;

				Traverse traverse = Traverse.Create(clone);
				traverse.Field("identifier").SetValue(def.id);
				traverse.Field("rarity").SetValue(def.rarity);
				traverse.Field("cost").SetValue(def.cost);
				// no vanilla relic sets generic, and UICardRelic's branch for it never paints relicBG
				traverse.Field("generic").SetValue(false);
				traverse.Field("racial").SetValue(false);
				traverse.Field("unlockedByDefault").SetValue(true);
				traverse.Field("triggerOnLoad").SetValue(false);
				traverse.Field("triggerLater").SetValue(false);
				traverse.Field("requiredKingdomLevel").SetValue(def.requiredKingdomLevel);
				traverse.Field("upgrade").SetValue(null);
				// any true makes it a kingdom relic, all false a boss chest relic
				traverse.Field("soldAtCartographer1").SetValue(!def.bossOnly);
				traverse.Field("soldAtCartographer2").SetValue(!def.bossOnly);
				traverse.Field("soldAtCartographer3").SetValue(!def.bossOnly);
				traverse.Field("soldAtCartographer4").SetValue(!def.bossOnly);

				traverse.Field("requiredBuilding").SetValue(BuildingType.None);
				traverse.Field("relatedUnit").SetValue("");
				traverse.Field("kingdom").SetValue(Races.None);
				traverse.Field("keywords").SetValue(new EffectKeywordType[0]);

				// the donor's effect dies at the attack, base FindTargets returns false
				// never at the cooldown, 9999999 is the passive marker and TriggerRelic zeroes it
				traverse.Field("attack").SetValue(new Attack());
				traverse.Field("cooldown").SetValue(1E+09f);

				// Icons\<RelicClass>.png embedded in the dll, HideAndDontSave survives the scene load
				// ppu comes off the donor, the world drop and drag ghost size themselves by it
				Sprite donorIcon = donor.GetIcon();
				Stream stream = typeof(Plugin).Assembly.GetManifestResourceStream("MoreRelics.Icons." + entry.GetType().Name + ".png");
				if (stream != null)
				{
					byte[] png = new byte[stream.Length];
					stream.Read(png, 0, png.Length);
					stream.Dispose();
					Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
					if (texture.LoadImage(png))
					{
						texture.filterMode = FilterMode.Point;
						texture.hideFlags = HideFlags.HideAndDontSave;
						float perUnit = ((donorIcon != null) ? (donorIcon.pixelsPerUnit * ((float)texture.width / donorIcon.rect.width)) : 100f);
						Sprite icon = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), perUnit);
						icon.hideFlags = HideFlags.HideAndDontSave;
						traverse.Field("icon").SetValue(icon);
						// vanilla bakes the sprite into the prefab's own Image, which the hud column draws
						Image image = clone.GetComponent<Image>();
						if (image != null)
						{
							image.sprite = icon;
						}
					}
					else
					{
						Plugin.Log?.LogError("could not decode the icon png for " + def.id);
					}
				}

				relics[def.id] = clone;
				if (!identifiers.Contains(def.id))
				{
					identifiers.Add(def.id);
				}
				Plugin.Log?.LogInfo("registered " + def.id + " cloned from " + donor.name + ", donor icon "
					+ ((donorIcon != null) ? (donorIcon.rect.width + "x" + donorIcon.rect.height + " @ " + donorIcon.pixelsPerUnit + " ppu") : "none"));
			}
		}

		// first donor that exists, a renamed prefab degrades instead of failing
		private static Relic Donor(Dictionary<string, Relic> relics, RelicDef def)
		{
			foreach (string name in def.cloneFrom)
			{
				if (relics.TryGetValue(name, out var found) && found != null)
				{
					return found;
				}
			}
			foreach (KeyValuePair<string, Relic> any in relics)
			{
				if (any.Value != null && Registry.Find(any.Key) == null)
				{
					return any.Value;
				}
			}
			return null;
		}
	}

	// Instantiate copies activeSelf and the prototype is parked inactive, so the hud column skips it
	[HarmonyPatch(typeof(RelicManager), "SpawnRelic", new System.Type[] { typeof(Relic) })]
	internal static class Patch_SpawnActive
	{
		private static void Postfix(Relic __result)
		{
			if (__result != null && Registry.Find(__result.GetIdentifier()) != null)
			{
				__result.gameObject.SetActive(true);
			}
		}
	}

	// UnlockRelic would write our ids into the permanent save and leave them after an uninstall
	[HarmonyPatch(typeof(RelicManager), "IsUnlocked")]
	internal static class Patch_IsUnlocked
	{
		private static void Postfix(string relic, ref bool __result)
		{
			// OR, never assign, another relic mod has to compose with us
			__result = __result || Registry.Find(relic) != null;
		}
	}

	// a starting relic that no longer exists NREs the run, AddRelic returns null and nothing checks
	[HarmonyPatch(typeof(RaceDataManager), "GetStartingRelic")]
	internal static class Patch_StartingRelic
	{
		private static void Postfix(ref string __result)
		{
			if (!string.IsNullOrEmpty(__result) && AddressablesManager.Instance != null
				&& AddressablesManager.Instance.GetRelic(__result) == null)
			{
				Plugin.Log?.LogWarning("starting relic " + __result + " does not exist, dropping it");
				__result = "";
			}
		}
	}

	// ------------------------------ boss only ------------------------------
	// the boss pool wants relic.kingdom == currentRace, the armory and the world map want None
	// a GetKingdom postfix did NOT reach it, and registration cannot write it - no race yet
	internal static class BossOnly
	{
		public static void Stamp()
		{
			if (RaceManager.Instance == null || AddressablesManager.Instance == null)
			{
				return;
			}
			Races race = RaceManager.Instance.GetRace();
			foreach (RelicEntry entry in Registry.Entries)
			{
				if (!entry.Def.bossOnly || !entry.Available)
				{
					continue;
				}
				Relic relic = AddressablesManager.Instance.GetRelic(entry.Def.id);
				if (relic != null)
				{
					Traverse.Create(relic).Field("kingdom").SetValue(race);
				}
			}
		}
	}

	// the subclasses override PreparePool with no base call, so this is the boss chest alone
	[HarmonyPatch(typeof(DropBossChest), "PreparePool")]
	internal static class Patch_BossPoolStamp
	{
		private static void Prefix()
		{
			try
			{
				BossOnly.Stamp();
			}
			catch (System.Exception e)
			{
				Plugin.Log?.LogError("boss pool stamp failed: " + e);
			}
		}
	}

	// LocalizedString cannot be authored at runtime, so our ids carry plain english
	[HarmonyPatch(typeof(Item), "GetTitle")]
	internal static class Patch_Title
	{
		private static void Postfix(Item __instance, ref string __result)
		{
			RelicEntry entry = Registry.Find(__instance.GetIdentifier());
			if (entry != null)
			{
				__result = entry.Def.title;
			}
		}
	}

	[HarmonyPatch(typeof(Item), "GetDescription")]
	internal static class Patch_Description
	{
		private static void Postfix(Item __instance, ref string __result)
		{
			RelicEntry entry = Registry.Find(__instance.GetIdentifier());
			if (entry != null)
			{
				__result = entry.Def.description;
			}
		}
	}

	// ------------------------------ hooks ------------------------------
	[HarmonyPatch(typeof(RelicManager), "Awake")]
	internal static class Patch_Hook
	{
		private static int s_LastDay = -1;

		private static void Postfix()
		{
			// RelicManager is per GameScene, so the day guard resets between runs
			s_LastDay = -1;
			// the armory reads the kingdom field long before any boss chest opens
			try
			{
				BossOnly.Stamp();
			}
			catch (System.Exception e)
			{
				Plugin.Log?.LogError("boss only stamp failed: " + e);
			}
			// the wave the morning taxes drop in, OnMorningStart waits out the day caption first
			DaytimeManager.BeforeMorningStart -= Morning;
			DaytimeManager.BeforeMorningStart += Morning;
			RelicManager.OnRelicFound -= Found;
			RelicManager.OnRelicFound += Found;
		}

		// AddRelic only, a save restore is covered by OnMorning
		private static void Found(Relic relic)
		{
			RelicEntry entry = ((relic != null) ? Registry.Find(relic.GetIdentifier()) : null);
			if (entry == null)
			{
				return;
			}
			try
			{
				entry.OnAcquired();
			}
			catch (System.Exception e)
			{
				Plugin.Log?.LogError(entry.Def.id + " failed on pickup: " + e);
			}
		}

		// it also fires on load, hence the day guard
		private static void Morning(int day)
		{
			if (day == s_LastDay)
			{
				return;
			}
			s_LastDay = day;
			foreach (RelicEntry entry in Registry.Entries)
			{
				try
				{
					if (entry.Held())
					{
						entry.OnMorning(day);
					}
				}
				catch (System.Exception e)
				{
					Plugin.Log?.LogError(entry.Def.id + " failed on morning: " + e);
				}
			}
		}
	}
}
