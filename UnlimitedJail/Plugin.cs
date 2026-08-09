using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.TitleScreen;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnlimitedJail
{
	[BepInPlugin("ownly.unlimitedjail", "Unlimited Jail", "1.0.0")]
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
		public static void Apply(UnlockedUnitsManager manager, string from)
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

			// a RECOVERED unit must lose its ban - the run save restores the list from when it WAS stashed
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
				Filter.Apply(__instance, "Awake");
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
				Filter.Apply(__instance, "SetBannedUnits");
			}
			catch
			{
			}
		}
	}

	[HarmonyPatch(typeof(TitleScreenManager), "Awake")]
	internal static class Patch_TitleScreenAwake
	{
		private static void Postfix(TitleScreenManager __instance)
		{
			try
			{
				__instance.gameObject.AddComponent<StashPanel>();
			}
			catch
			{
			}
		}
	}

	// ------------------------------ the jail takeover ------------------------------
	// cells open the panel and display what is stashed. nothing is written to the kingdom save
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
					+ " - the mod frees it, stash it from the panel to keep it held back");
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
			// unreachable today, kept for a game update dropping a prefab already in the stash
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

	// that method is what activates the vanilla catalog overlay, so skipping it IS the replacement
	[HarmonyPatch(typeof(TitleScreenHeroSelectionManager), "OpenJailSelection")]
	internal static class Patch_OpenJailSelection
	{
		private static bool Prefix()
		{
			try
			{
				StashPanel panel = Object.FindObjectOfType<StashPanel>();
				if (panel == null)
				{
					return true;
				}
				panel.OpenFromJail();
				return false;
			}
			catch (System.Exception e)
			{
				Plugin.Log?.LogError("could not open the stash from the jail: " + e);
				return true;
			}
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

	// a coin per jailed unit, charged where vanilla charges the starting relic and unit:
	// the kingdom counter and its catalogs through GetCoinsRemaining, then the run purse
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

		// vanilla hands you three cells, so the first three stay free.
		// shared, so the counter on screen and the charge on the purse cannot drift
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

	// ------------------------------ title screen overlay ------------------------------
	// rebuilt on every open, so switching kingdom or profile can never leave it stale
	internal class StashPanel : MonoBehaviour
	{
		private const float TileSize = 52f;
		private const float TileGap = 6f;
		private const float LabelHeight = 15f;
		private const float Margin = 22f;
		private const float TopBar = 28f;
		private const float StashedFade = 0.3f;
		private const string OutlinedFont = "Passage7Outline";
		private const int SortingOrder = 30000;
		private const float PanelScale = 1.5f;

		// colour-picked off the game's own screenshots: 060608 ui dark, 322b28 wood, c8ae8c parchment
		private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.75f);
		private static readonly Color PanelColor = new Color(0.024f, 0.024f, 0.031f, 0.98f);
		private static readonly Color TileColor = new Color(0.196f, 0.169f, 0.157f, 1f);
		private static readonly Color TileStashedColor = new Color(0.094f, 0.078f, 0.075f, 1f);
		private static readonly Color LabelColor = new Color(0.784f, 0.682f, 0.549f, 1f);

		private sealed class Tile
		{
			public string id;
			public Image background;
			public Image icon;
			public TextMeshProUGUI label;
		}

		private readonly List<Tile> m_Tiles = new List<Tile>();
		private GameObject m_Root;
		private TMP_FontAsset m_Font;

		internal void OpenFromJail()
		{
			// ClickJail fires OpenJailSelection twice for an "empty" cell, and every cell is empty now
			if (m_Root != null)
			{
				return;
			}
			Selectables(enable: false);
			Open();
		}

		private void OnDestroy()
		{
			if (m_Root != null)
			{
				Close();
			}
		}

		private void Open()
		{
			try
			{
				Build();
			}
			catch (System.Exception e)
			{
				// never silent - a bare catch here once hid a one line bug behind "the panel never opens"
				Plugin.Log?.LogError("stash panel failed to build: " + e);
				Close();
			}
		}

		private void Build()
		{
			List<string> pool = new List<string> { "Cyclops", "Archer", "Runescribe" };
			Merge(pool, PermanentDataManager.Instance.GetUnlockedUnits());
			Merge(pool, RaceDataManager.Instance.GetUnlockedUnits());
			pool.Sort();

			// unobtainable units are dropped
			List<string> ids = new List<string>();
			List<Unit> units = new List<Unit>();
			foreach (string id in pool)
			{
				Unit unit = AddressablesManager.Instance.GetUnit(id);
				if (unit != null)
				{
					ids.Add(id);
					units.Add(unit);
				}
			}
			if (ids.Count == 0)
			{
				return;
			}

			m_Font = FindFont();

			// as square as it goes - 2x1, 2x2, 3x2, 3x3, 4x3, 4x4, 5x4 - plus a column, tiles are taller than wide
			float tileHeight = TileSize + LabelHeight;
			int columns = 1;
			int rows = 1;
			while (columns * rows < ids.Count)
			{
				if (columns > rows)
				{
					rows++;
				}
				else
				{
					columns++;
				}
			}
			columns++;
			rows = Mathf.CeilToInt((float)ids.Count / columns);
			float gridWidth = columns * TileSize + (columns - 1) * TileGap;
			float gridHeight = rows * tileHeight + (rows - 1) * TileGap;

			// nested, not a root canvas - a root one does not inherit the ui scaling
			Canvas host = BusiestOverlayCanvas();
			m_Root = new GameObject("UnlimitedJail", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
			RectTransform rootRect = m_Root.GetComponent<RectTransform>();
			rootRect.SetParent(host.transform, false);
			rootRect.anchorMin = Vector2.zero;
			rootRect.anchorMax = Vector2.one;
			rootRect.offsetMin = Vector2.zero;
			rootRect.offsetMax = Vector2.zero;
			Canvas own = m_Root.GetComponent<Canvas>();
			own.overrideSorting = true;
			own.sortingOrder = SortingOrder;

			RectTransform root = NewRect("Backdrop", m_Root.transform);
			root.anchorMin = Vector2.zero;
			root.anchorMax = Vector2.one;
			root.offsetMin = Vector2.zero;
			root.offsetMax = Vector2.zero;
			root.gameObject.AddComponent<Image>().color = BackdropColor;
			root.gameObject.AddComponent<Button>().onClick.AddListener(Close);

			RectTransform panel = NewRect("Panel", root);
			panel.anchorMin = new Vector2(0.5f, 0.5f);
			panel.anchorMax = new Vector2(0.5f, 0.5f);
			panel.pivot = new Vector2(0.5f, 0.5f);
			panel.sizeDelta = new Vector2(gridWidth + 2f * Margin, gridHeight + TopBar + 2f * Margin);
			panel.anchoredPosition = Vector2.zero;
			panel.localScale = Vector3.one * PanelScale;
			panel.gameObject.AddComponent<Image>().color = PanelColor;

			RectTransform close = NewRect("Close", panel);
			close.anchorMin = new Vector2(1f, 1f);
			close.anchorMax = new Vector2(1f, 1f);
			close.pivot = new Vector2(1f, 1f);
			close.sizeDelta = new Vector2(22f, 22f);
			close.anchoredPosition = new Vector2(-5f, -5f);
			close.gameObject.AddComponent<Image>().color = TileColor;
			close.gameObject.AddComponent<Button>().onClick.AddListener(Close);

			// unity allows one Graphic per object, so the X sits on a child of the button
			RectTransform closeLabel = NewRect("Label", close);
			closeLabel.anchorMin = Vector2.zero;
			closeLabel.anchorMax = Vector2.one;
			closeLabel.offsetMin = Vector2.zero;
			closeLabel.offsetMax = Vector2.zero;
			AddText(closeLabel, TextAlignmentOptions.Center, 18f).text = "X";

			for (int i = 0; i < ids.Count; i++)
			{
				int column = i % columns;
				int row = i / columns;

				RectTransform tileRect = NewRect(ids[i], panel);
				tileRect.anchorMin = new Vector2(0f, 1f);
				tileRect.anchorMax = new Vector2(0f, 1f);
				tileRect.pivot = new Vector2(0f, 1f);
				tileRect.sizeDelta = new Vector2(TileSize, tileHeight);
				tileRect.anchoredPosition = new Vector2(
					Margin + column * (TileSize + TileGap),
					-(Margin + TopBar + row * (tileHeight + TileGap)));

				Tile tile = new Tile { id = ids[i] };
				tile.background = tileRect.gameObject.AddComponent<Image>();

				RectTransform iconRect = NewRect("Icon", tileRect);
				iconRect.anchorMin = new Vector2(0f, 1f);
				iconRect.anchorMax = new Vector2(1f, 1f);
				iconRect.pivot = new Vector2(0.5f, 1f);
				iconRect.offsetMin = new Vector2(3f, 0f);
				iconRect.offsetMax = new Vector2(-3f, 0f);
				iconRect.sizeDelta = new Vector2(iconRect.sizeDelta.x, TileSize - 4f);
				iconRect.anchoredPosition = new Vector2(0f, -2f);
				tile.icon = iconRect.gameObject.AddComponent<Image>();
				tile.icon.sprite = units[i].GetIcon();
				tile.icon.preserveAspect = true;
				tile.icon.raycastTarget = false;

				RectTransform labelRect = NewRect("Label", tileRect);
				labelRect.anchorMin = new Vector2(0f, 0f);
				labelRect.anchorMax = new Vector2(1f, 0f);
				labelRect.pivot = new Vector2(0.5f, 0f);
				labelRect.offsetMin = new Vector2(1f, 0f);
				labelRect.offsetMax = new Vector2(-1f, 0f);
				labelRect.sizeDelta = new Vector2(labelRect.sizeDelta.x, LabelHeight);
				labelRect.anchoredPosition = Vector2.zero;
				tile.label = AddText(labelRect, TextAlignmentOptions.Center, 11f);
				tile.label.text = units[i].GetName();

				string id = ids[i];
				tileRect.gameObject.AddComponent<Button>().onClick.AddListener(delegate
				{
					Stash.Toggle(id);
					Paint();
				});

				m_Tiles.Add(tile);
			}

			Paint();
		}

		private void Close()
		{
			m_Tiles.Clear();
			if (m_Root != null)
			{
				Object.Destroy(m_Root);
				m_Root = null;
			}
			Selectables(enable: true);
			try
			{
				TitleScreenJail jail = Object.FindObjectOfType<TitleScreenJail>();
				if (jail != null)
				{
					jail.Decorate();
				}
			}
			catch
			{
			}
		}

		// null check is for OnDestroy: Instance is never cleared, so the static outlives the object
		private static void Selectables(bool enable)
		{
			TitleScreenHeroSelectionManager screen = TitleScreenHeroSelectionManager.Instance;
			if (screen != null)
			{
				screen.SetSelectablesEnabled(enable);
			}
		}

		private void Paint()
		{
			foreach (Tile tile in m_Tiles)
			{
				bool stashed = Stash.Contains(tile.id);
				float alpha = stashed ? StashedFade : 1f;
				tile.background.color = stashed ? TileStashedColor : TileColor;
				tile.icon.color = new Color(1f, 1f, 1f, alpha);
				tile.label.color = new Color(1f, 1f, 1f, alpha);
			}

			// update coin counter immediately
			TitleScreenHeroSelectionManager screen = TitleScreenHeroSelectionManager.Instance;
			if (screen != null)
			{
				screen.UpdateKingdomData();
			}
		}

		private static RectTransform NewRect(string name, Transform parent)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.SetParent(parent, false);
			return rect;
		}

		private TextMeshProUGUI AddText(RectTransform parent, TextAlignmentOptions alignment, float size)
		{
			TextMeshProUGUI text = parent.gameObject.AddComponent<TextMeshProUGUI>();
			if (m_Font != null)
			{
				text.font = m_Font;
				text.fontSharedMaterial = m_Font.material;
			}
			text.fontSize = size;
			text.alignment = alignment;
			text.enableWordWrapping = false;
			text.overflowMode = TextOverflowModes.Ellipsis;
			text.color = LabelColor;
			text.raycastTarget = false;
			return text;
		}

		// the outline is baked into the atlas, the bitmap shader has no outline properties at all
		internal static TMP_FontAsset FindFont()
		{
			TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
			foreach (TMP_FontAsset font in fonts)
			{
				if (font.name == OutlinedFont)
				{
					return font;
				}
			}
			return fonts.Length > 0 ? fonts[0] : null;
		}

		private static Canvas BusiestOverlayCanvas()
		{
			Canvas best = null;
			int bestChildren = -1;
			foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>())
			{
				if (canvas.renderMode != RenderMode.ScreenSpaceOverlay || !canvas.isRootCanvas)
				{
					continue;
				}
				if (canvas.transform.childCount > bestChildren)
				{
					best = canvas;
					bestChildren = canvas.transform.childCount;
				}
			}
			return best;
		}

		private static void Merge(List<string> pool, List<string> extra)
		{
			if (extra == null)
			{
				return;
			}
			foreach (string id in extra)
			{
				if (!pool.Contains(id))
				{
					pool.Add(id);
				}
			}
		}
	}
}
