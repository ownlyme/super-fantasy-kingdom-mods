using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using FMODUnity;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.TitleScreen;
using SuperFantasyKingdom.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UnlimitedJail
{
	[BepInPlugin("ownly.unlimitedjail", "Unlimited Jail", "1.1.0")]
	public class Plugin : BaseUnityPlugin
	{
		internal static ManualLogSource Log;

		private void Awake()
		{
			Log = Logger;
			new Harmony(Info.Metadata.GUID).PatchAll();
		}
	}

	// ------------------------------ pool filtering ------------------------------
	// TWO gates, covering disjoint offer paths:
	//   m_UnlockedUnits - reward cards, hermit / monster hunter / researcher, prison, merchant, portal
	//   m_Banned        - guilds and UIOverlayMerchant, which never read the unlock list at all
	internal static class Filter
	{
		public static void Apply(UnlockedUnitsManager manager)
		{
			// no early out on an empty stash - recovering the LAST unit still has bans to lift
			List<string> stashed = Stash.Units;
			Traverse traverse = Traverse.Create(manager);
			List<string> unlocked = traverse.Field("m_UnlockedUnits").GetValue<List<string>>();
			List<string> banned = traverse.Field("m_Banned").GetValue<List<string>>();
			foreach (string id in stashed)
			{
				unlocked.Remove(id);
				if (!banned.Contains(id))
				{
					banned.Add(id);
				}
			}

			// a RECOVERED unit must lose its ban - the run save restores the list from when it WAS stashed.
			// safe to clear: forcing the jail to "empty" makes us the only source of a real name
			banned.RemoveAll((string id) => id != Jail.Empty && !stashed.Contains(id));
		}
	}

	[HarmonyPatch(typeof(UnlockedUnitsManager), "Awake")]
	internal static class Patch_UnlockedUnitsAwake
	{
		private static void Postfix(UnlockedUnitsManager __instance)
		{
			try
			{
				Filter.Apply(__instance);
			}
			catch
			{
			}
		}
	}

	// loading a run replaces the ban list wholesale. the only place a stale ban is lifted, keep it
	[HarmonyPatch(typeof(UnlockedUnitsManager), "SetBannedUnits")]
	internal static class Patch_SetBannedUnits
	{
		private static void Postfix(UnlockedUnitsManager __instance)
		{
			try
			{
				Filter.Apply(__instance);
			}
			catch
			{
			}
		}
	}

	// ------------------------------ the jail takeover ------------------------------
	// cells open the vanilla selection catalog and show what is stashed, writing nothing to the save
	internal static class Jail
	{
		internal const string Empty = "empty";
		private static readonly HashSet<string> s_Warned = new HashSet<string>();

		// "empty" sends ClickJail down the selection branch at any kingdom level, and bans nothing
		public static string Force(string jailed)
		{
			if (!string.IsNullOrEmpty(jailed) && jailed != Empty && s_Warned.Add(jailed))
			{
				Plugin.Log?.LogWarning("jail slot held " + jailed
					+ " - the mod frees it, jail it from the selection screen to keep it held back");
			}
			return Empty;
		}

		public static void Paint(TitleScreenJail jail, int index)
		{
			List<string> stashed = Stash.Units;
			Traverse traverse = Traverse.Create(jail);
			GameObject cell = traverse.Field("jail" + index).GetValue<GameObject>();
			GameObject rubble = traverse.Field("jail" + index + "Rubble").GetValue<GameObject>();
			Image door = traverse.Field("jail" + index + "Door").GetValue<Image>();
			ImageAnimator animator = traverse.Field("jail" + index + "Jailed").GetValue<ImageAnimator>();
			if (stashed.Count < index)
			{
				return;
			}
			Unit unit = AddressablesManager.Instance.GetUnit(stashed[index - 1]);
			if (unit == null)
			{
				Plugin.Log?.LogWarning("stashed unit has no prefab: " + stashed[index - 1]);
				return;
			}
			cell.SetActive(value: true);
			rubble.SetActive(value: false);
			door.sprite = traverse.Field("spriteDoorClosed").GetValue<Sprite>();
			animator.SetSprites(unit.GetIdleSprites());
			animator.gameObject.SetActive(value: true);
		}
	}

	[HarmonyPatch(typeof(RaceDataManager), "GetJail1")]
	internal static class Patch_GetJail1
	{
		private static void Postfix(ref string __result)
		{
			__result = Jail.Force(__result);
		}
	}

	[HarmonyPatch(typeof(RaceDataManager), "GetJail2")]
	internal static class Patch_GetJail2
	{
		private static void Postfix(ref string __result)
		{
			__result = Jail.Force(__result);
		}
	}

	[HarmonyPatch(typeof(RaceDataManager), "GetJail3")]
	internal static class Patch_GetJail3
	{
		private static void Postfix(ref string __result)
		{
			__result = Jail.Force(__result);
		}
	}

	[HarmonyPatch(typeof(TitleScreenJail), "ShowJail1")]
	internal static class Patch_ShowJail1
	{
		private static void Postfix(TitleScreenJail __instance)
		{
			try
			{
				Jail.Paint(__instance, 1);
			}
			catch
			{
			}
		}
	}

	[HarmonyPatch(typeof(TitleScreenJail), "ShowJail2")]
	internal static class Patch_ShowJail2
	{
		private static void Postfix(TitleScreenJail __instance)
		{
			try
			{
				Jail.Paint(__instance, 2);
			}
			catch
			{
			}
		}
	}

	[HarmonyPatch(typeof(TitleScreenJail), "ShowJail3")]
	internal static class Patch_ShowJail3
	{
		private static void Postfix(TitleScreenJail __instance)
		{
			try
			{
				Jail.Paint(__instance, 3);
			}
			catch
			{
			}
		}
	}

	// the ONLY writer of a jail slot, and it must never run - the stash lives outside the save.
	// stands in for vanilla's other two jobs, restoring gamepad focus and repainting the cells
	[HarmonyPatch(typeof(TitleScreenJail), "SetJailed")]
	internal static class Patch_SetJailed
	{
		private static bool Prefix(TitleScreenJail __instance)
		{
			try
			{
				Traverse traverse = Traverse.Create(__instance);
				int cell = traverse.Field("m_SelectedCell").GetValue<int>();
				if (cell >= 1 && cell <= 3)
				{
					Button button = traverse.Field("jail" + cell + "Button").GetValue<Button>();
					if (button != null)
					{
						GamepadManager.Instance.SetGameObjectToSelect(button.gameObject, force: true);
					}
				}
				traverse.Field("m_SelectedCell").SetValue(0);
				__instance.Decorate();
			}
			catch
			{
			}
			return false;
		}
	}

	// ------------------------------ the selection catalog ------------------------------
	// vanilla's jail catalog, kept open: a click toggles the unit instead of filling a cell
	internal static class Catalog
	{
		// red already means banished here, and cannot read as an unplayed unit's dark silhouette
		private static readonly Color JailedTile = new Color(0.60f, 0.24f, 0.22f, 1f);
		private static readonly Color JailedIcon = new Color(0.38f, 0.16f, 0.15f, 1f);

		// Generate destroys the old buttons, so leftover entries go null rather than stale
		private static readonly Dictionary<string, GameObject> s_Entries = new Dictionary<string, GameObject>();

		public static void Track(string key, GameObject button)
		{
			s_Entries[key] = button;
			Paint(key);
		}

		public static void Paint(string key)
		{
			GameObject button;
			if (!s_Entries.TryGetValue(key, out button) || button == null)
			{
				return;
			}
			bool jailed = Stash.Contains(key);
			Image tile = button.GetComponent<Image>();
			if (tile != null)
			{
				tile.color = jailed ? JailedTile : Color.white;
			}
			Transform icon = button.transform.Find("Icon");
			if (icon != null)
			{
				icon.GetComponent<Image>().color = jailed ? JailedIcon : Color.white;
			}
		}
	}

	[HarmonyPatch(typeof(UICatalogJail), "Unit")]
	internal static class Patch_CatalogUnit
	{
		private static void Postfix(string key, GameObject button)
		{
			try
			{
				Catalog.Track(key, button);
			}
			catch
			{
			}
		}
	}

	// a unit stashed before this list narrowed must stay reachable, or it can never be recovered
	[HarmonyPatch(typeof(UICatalogJail), "CanAddUnit")]
	internal static class Patch_CanAddUnit
	{
		private static void Postfix(Unit unit, ref bool __result)
		{
			try
			{
				if (!__result && Stash.Contains(unit.GetEntityIdentifier()))
				{
					__result = true;
				}
			}
			catch
			{
			}
		}
	}

	// ------------------------------ the gold counter ------------------------------
	// the throne room's own coin widget cloned into the catalog's top left corner,
	// since the kingdom screen's counter sits behind a full screen overlay here
	internal static class Coins
	{
		private const float Margin = 14f;
		private const int WalkLimit = 4;

		private static GameObject s_Widget;
		private static Text s_Text;

		public static void Show()
		{
			if (s_Widget != null)
			{
				return;
			}
			TitleScreenHeroSelectionManager screen = TitleScreenHeroSelectionManager.Instance;
			if (screen == null)
			{
				return;
			}
			Traverse throne = Traverse.Create(screen);
			Text coins = throne.Field("coins").GetValue<Text>();
			GameObject overlay = Traverse.Create(AchievementManager.Instance)
				.Field("achievementOverlay").GetValue<GameObject>();
			if (coins == null || overlay == null || overlay.GetComponent<RectTransform>() == null)
			{
				Plugin.Log?.LogWarning("no coin widget to clone, the catalog opens without a counter");
				return;
			}

			Transform source = WidgetRoot(throne, coins.transform);
			if (source == coins.transform)
			{
				Plugin.Log?.LogWarning("the coin counter has no widget around it, cloning the number alone");
			}

			// size before the anchors move - a stretched rect reads sizeDelta as an offset, not a size
			RectTransform sourceRect = source.GetComponent<RectTransform>();
			Vector2 size = sourceRect != null
				? new Vector2(sourceRect.rect.width, sourceRect.rect.height)
				: Vector2.zero;

			s_Widget = Object.Instantiate(source.gameObject, overlay.transform);
			s_Widget.name = "UnlimitedJailCoins";
			RectTransform rect = s_Widget.GetComponent<RectTransform>();
			if (rect != null)
			{
				rect.anchorMin = new Vector2(0f, 1f);
				rect.anchorMax = new Vector2(0f, 1f);
				rect.pivot = new Vector2(0f, 1f);
				rect.sizeDelta = size;
				rect.anchoredPosition = new Vector2(Margin, 0f - Margin);
			}
			foreach (Graphic graphic in s_Widget.GetComponentsInChildren<Graphic>(includeInactive: true))
			{
				graphic.raycastTarget = false;
			}
			s_Text = s_Widget.GetComponentInChildren<Text>(includeInactive: true);
			Refresh();
		}

		public static void Hide()
		{
			if (s_Widget != null)
			{
				Object.Destroy(s_Widget);
				s_Widget = null;
				s_Text = null;
			}
		}

		public static void Refresh()
		{
			if (s_Text != null)
			{
				s_Text.text = TitleScreenHeroSelectionManager.Instance.GetCoinsRemaining().ToString();
			}
		}

		// largest subtree holding the coin counter and none of its siblings, whatever the nesting.
		// the canvas and step limits stop a missing sibling from cloning half the screen
		private static Transform WidgetRoot(Traverse throne, Transform coins)
		{
			Transform[] siblings = new Transform[]
			{
				Counter(throne, "shards"),
				Counter(throne, "faith"),
				Counter(throne, "superMetal")
			};
			if (siblings[0] == null && siblings[1] == null && siblings[2] == null)
			{
				return coins;
			}
			Transform widget = coins;
			for (int step = 0; step < WalkLimit; step++)
			{
				Transform parent = widget.parent;
				if (parent == null || parent.GetComponent<Canvas>() != null || Holds(parent, siblings))
				{
					break;
				}
				widget = parent;
			}
			return widget;
		}

		private static bool Holds(Transform parent, Transform[] siblings)
		{
			foreach (Transform sibling in siblings)
			{
				if (sibling != null && sibling.IsChildOf(parent))
				{
					return true;
				}
			}
			return false;
		}

		private static Transform Counter(Traverse throne, string field)
		{
			Text text = throne.Field(field).GetValue<Text>();
			return text != null ? text.transform : null;
		}
	}

	[HarmonyPatch(typeof(AchievementManager), "OpenJail")]
	internal static class Patch_OpenJail
	{
		private static void Postfix()
		{
			try
			{
				Coins.Show();
			}
			catch (System.Exception e)
			{
				Plugin.Log?.LogError("could not build the coin counter: " + e);
			}
		}
	}

	[HarmonyPatch(typeof(AchievementManager), "Close")]
	internal static class Patch_AchievementClose
	{
		private static void Postfix(bool __result)
		{
			try
			{
				if (__result)
				{
					Coins.Hide();
				}
			}
			catch
			{
			}
		}
	}

	// cancel on a unit's details screen should land back on the grid, not the throne room.
	// OpenUnitDetails calls CloseCatalog first, which zeroes m_WasOpen, so vanilla's own
	// "back to the grid" branch in UICatalogManager.Close can never fire
	[HarmonyPatch(typeof(TitleScreenHeroSelectionManager), "CloseJailSelection")]
	internal static class Patch_CloseJailSelection
	{
		private static bool Prefix()
		{
			try
			{
				UICatalogManager catalog = UICatalogManager.Instance;
				if (catalog == null || !catalog.IsUnitDetailsOpen())
				{
					return true;
				}
				// mirrors OpenJail, which generates without EnableCatalog - the jail screen has no prev/next
				Traverse traverse = Traverse.Create(catalog);
				traverse.Method("CloseUnitDetails").GetValue();
				traverse.Method("Generate",
					new System.Type[] { typeof(CatalogType), typeof(int) },
					new object[] { CatalogType.Jail, 0 }).GetValue();
				RuntimeManager.PlayOneShot("event:/SFX/UI/Cancel");
				return false;
			}
			catch (System.Exception e)
			{
				Plugin.Log?.LogError("could not step back to the selection: " + e);
				return true;
			}
		}
	}

	// vanilla assigns the cell and closes. toggle and stay put instead
	[HarmonyPatch(typeof(TitleScreenHeroSelectionManager), "SelectJailedUnit")]
	internal static class Patch_SelectJailedUnit
	{
		private static bool Prefix(string key)
		{
			try
			{
				Stash.Toggle(key);
				Catalog.Paint(key);
				RuntimeManager.PlayOneShot(Stash.Contains(key)
					? "event:/SFX/UI/Confirm"
					: "event:/SFX/UI/Cancel");
				// clicking an already selected entry never re-fires Select, so refresh the card by hand
				UICatalogManager.Instance.OnSelect(key, clicked: false);
				TitleScreenHeroSelectionManager.Instance.UpdateKingdomData();
				Coins.Refresh();
			}
			catch (System.Exception e)
			{
				// never fall through - vanilla would write the cell to the save and close the screen
				Plugin.Log?.LogError("could not toggle " + key + ": " + e);
			}
			return false;
		}
	}

	// ------------------------------ coin cost ------------------------------
	// a coin per jailed unit, charged where vanilla charges the starting relic and unit:
	// the kingdom counter and its catalogs, then the run purse
	[HarmonyPatch(typeof(TitleScreenHeroSelectionManager), "GetCoinsRemaining")]
	internal static class Patch_CoinsRemaining
	{
		private static void Postfix(ref int __result)
		{
			try
			{
				__result = Mathf.Max(0, __result - Stash.CoinCost);
			}
			catch
			{
			}
		}
	}

	[HarmonyPatch(typeof(ResourceManager), "GiveStartingResources")]
	internal static class Patch_StartingCoins
	{
		private static void Postfix(ResourceManager __instance)
		{
			try
			{
				int spend = Mathf.Min(Stash.CoinCost, __instance.GetCoins());
				if (spend > 0)
				{
					__instance.SpendResource(ResourceType.Coins, spend);
				}
			}
			catch
			{
			}
		}
	}

	// ------------------------------ stashed unit ids ------------------------------
	// one file per profile + kingdom, never inside the game's saves - the unlock list stays complete
	internal static class Stash
	{
		private static readonly List<string> s_Units = new List<string>();
		private static string s_Key;

		public static List<string> Units
		{
			get
			{
				Reload();
				return s_Units;
			}
		}

		// vanilla hands you three cells free. shared, so the counter and the charge cannot drift
		public static int CoinCost
		{
			get { return Mathf.Max(0, Units.Count - 3); }
		}

		public static bool Contains(string entityIdentifier)
		{
			return Units.Contains(entityIdentifier);
		}

		public static void Toggle(string entityIdentifier)
		{
			Reload();
			if (!s_Units.Remove(entityIdentifier))
			{
				s_Units.Add(entityIdentifier);
			}
			Write();
		}

		private static void Reload()
		{
			string key = CurrentKey();
			if (key == s_Key)
			{
				return;
			}
			s_Key = key;
			s_Units.Clear();
			try
			{
				string path = PathFor(key);
				if (!File.Exists(path))
				{
					return;
				}
				foreach (string line in File.ReadAllLines(path))
				{
					string id = line.Trim();
					if (id.Length > 0 && !s_Units.Contains(id))
					{
						s_Units.Add(id);
					}
				}
			}
			catch
			{
			}
		}

		private static void Write()
		{
			try
			{
				string path = PathFor(s_Key);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllLines(path, s_Units.ToArray());
			}
			catch
			{
			}
		}

		// matches the game's own save naming, human_1 / undead_2
		private static string CurrentKey()
		{
			try
			{
				string race = Helper.Instance.GetRaceString(MainManager.Instance.GetSelectedKingdom());
				return race + "_" + (SettingsManager.Instance.GetProfile() + 1);
			}
			catch
			{
				return "unknown";
			}
		}

		private static string PathFor(string key)
		{
			return Path.Combine(Path.Combine(Paths.ConfigPath, "UnlimitedJail"), key + ".txt");
		}
	}
}
