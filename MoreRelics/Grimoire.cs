using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Spells;
using SuperFantasyKingdom.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MoreRelics
{
	// ------------------------------ grimoire ------------------------------
	// vanilla holds one city and one combat spell and every path replaces rather than adds
	internal sealed class Grimoire : RelicEntry
	{
		private static readonly RelicDef Definition = new RelicDef
		{
			id = "ownly_Grimoire",
			cloneFrom = new[] { "GreenHouse", "BetterBerries", "Fruitarian", "SpawnTrees" },
			rarity = Rarity.Legendary,
			cost = 9,
			title = "Grimoire",
			description = "A new spell no longer replaces the old one. Click the second slot to switch."
		};

		public override RelicDef Def
		{
			get { return Definition; }
		}

		public override void OnAcquired()
		{
			Preview.Refresh();
		}

		// covers a save restore, the morning fires on every load
		public override void OnMorning(int day)
		{
			Preview.Refresh();
		}

		internal static bool Active()
		{
			RelicEntry entry = Registry.Find(Definition.id);
			return entry != null && entry.Held();
		}

		// the cast path is keyed on SpellType, not on the slot field
		internal static void Swap()
		{
			SpellManager spells = SpellManager.Instance;
			if (spells == null)
			{
				return;
			}
			bool combat = spells.IsOnBattlefield();
			SpellBase active = (combat ? spells.GetCombatSpell() : spells.GetCitySpell());
			SpellType reserve = Reserve.Get(combat);
			if (active == null || reserve == SpellType.None || reserve == active.GetSpellType()
				|| spells.GetSpell(reserve) == null)
			{
				return;
			}

			// a spell mid-selection would keep targeting with the type we just moved out
			spells.Cancel(manual: false);
			Reserve.Set(combat, active.GetSpellType());
			// the SpellType overload, so the keep prefix on the SpellBase one cannot re-enter
			if (combat)
			{
				spells.SetCombatSpell(reserve);
			}
			else
			{
				spells.SetCitySpell(reserve);
			}

			// an event cannot be raised from outside the class that declares it
			Action<bool> onSwitch = Traverse.Create(typeof(SpellManager)).Field("OnSwitch").GetValue<Action<bool>>();
			onSwitch?.Invoke(combat);
			Preview.Refresh();
		}
	}

	// ------------------------------ the second slot ------------------------------
	// vanilla's spell icon is the only way to cast without a keyboard, so it keeps its click
	internal static class Preview
	{
		// of the spell icon, nudge these against a screenshot
		private const float Scale = 0.62f;
		private const float OffsetX = 0f;
		// negative is down
		private const float OffsetY = -0.85f;
		// a stretched rect reads sizeDelta as an offset, so fall back to this
		private const float FallbackSize = 64f;
		private static readonly Color Dimmed = new Color(1f, 1f, 1f, 0.8f);

		// 16x16, drawn here so the mod still ships as a bare dll
		private static readonly string[] UnknownArt =
		{
			"................",
			"................",
			".....######.....",
			"....########....",
			"...###....###...",
			"...###....###...",
			"..........###...",
			".........###....",
			"........###.....",
			".......###......",
			".......###......",
			".......###......",
			"................",
			".......###......",
			".......###......",
			"................"
		};

		private static Image s_Image;
		private static Sprite s_Unknown;

		internal static bool Hovering;

		internal static void Build(Image icon)
		{
			Hovering = false;
			RectTransform source = icon.rectTransform;

			GameObject slot = new GameObject("GrimoireReserve", typeof(RectTransform));
			slot.transform.SetParent(source.parent, worldPositionStays: false);

			// sizeDelta not rect, layout has not run yet inside Awake
			Vector2 size = source.sizeDelta;
			if (size.x <= 1f || size.y <= 1f)
			{
				size = new Vector2(FallbackSize, FallbackSize);
			}
			RectTransform rect = slot.GetComponent<RectTransform>();
			rect.anchorMin = source.anchorMin;
			rect.anchorMax = source.anchorMax;
			rect.pivot = source.pivot;
			rect.localScale = Vector3.one;
			rect.sizeDelta = size * Scale;
			rect.anchoredPosition = source.anchoredPosition
				+ new Vector2(size.x * OffsetX, size.y * OffsetY);

			s_Image = slot.AddComponent<Image>();
			s_Image.material = icon.material;
			s_Image.preserveAspect = true;
			s_Image.raycastTarget = true;

			EventTrigger trigger = slot.AddComponent<EventTrigger>();
			Hover(trigger, EventTriggerType.PointerEnter, over: true);
			Hover(trigger, EventTriggerType.PointerExit, over: false);
			Refresh();
		}

		internal static void Refresh()
		{
			// a destroyed unity object compares null, so the daily scene reload heals itself
			if (s_Image == null)
			{
				return;
			}
			bool active = Grimoire.Active();
			s_Image.gameObject.SetActive(active);
			if (!active)
			{
				return;
			}

			SpellManager spells = SpellManager.Instance;
			SpellType reserve = ((spells != null) ? Reserve.Get(spells.IsOnBattlefield()) : SpellType.None);
			SpellBase spell = ((spells != null && reserve != SpellType.None) ? spells.GetSpell(reserve) : null);
			if (spell != null)
			{
				s_Image.sprite = spell.GetIcon();
				s_Image.color = Color.white;
				return;
			}
			// nothing banked yet, and Swap already no-ops on an empty reserve
			s_Image.sprite = Unknown();
			s_Image.color = Dimmed;
		}

		private static void Hover(EventTrigger trigger, EventTriggerType type, bool over)
		{
			EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
			entry.callback.AddListener(delegate
			{
				Hovering = over;
			});
			trigger.triggers.Add(entry);
		}

		private static Sprite Unknown()
		{
			if (s_Unknown != null)
			{
				return s_Unknown;
			}
			int size = UnknownArt.Length;
			Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
			texture.filterMode = FilterMode.Point;
			texture.hideFlags = HideFlags.HideAndDontSave;
			for (int row = 0; row < size; row++)
			{
				for (int column = 0; column < size; column++)
				{
					// texture rows run bottom up
					texture.SetPixel(column, size - 1 - row,
						(UnknownArt[row][column] == '#') ? Color.white : Color.clear);
				}
			}
			texture.Apply();
			s_Unknown = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
			s_Unknown.hideFlags = HideFlags.HideAndDontSave;
			return s_Unknown;
		}
	}

	// gameData carries one spellCity and one spellCombat, and the GameScene reloads daily
	internal static class Reserve
	{
		private static SpellType s_City;
		private static SpellType s_Combat;
		private static string s_Key;
		// the run the two above belong to, so a new run cannot inherit them
		private static int s_Seed;

		// stable for a run and restored with the save, so Reset Day keeps its reserve
		private static int CurrentSeed()
		{
			return ((GameManager.Instance != null) ? GameManager.Instance.GetSeed() : 0);
		}

		public static SpellType Get(bool combat)
		{
			Load();
			return (combat ? s_Combat : s_City);
		}

		public static void Set(bool combat, SpellType spell)
		{
			Load();
			if (combat)
			{
				s_Combat = spell;
			}
			else
			{
				s_City = spell;
			}
			Write();
		}

		public static void Clear()
		{
			Load();
			s_City = SpellType.None;
			s_Combat = SpellType.None;
			Write();
		}

		private static string Key()
		{
			string race = ((RaceManager.Instance != null) ? RaceManager.Instance.GetRace().ToString() : "None");
			string profile = ((SettingsManager.Instance != null) ? SettingsManager.Instance.GetProfile().ToString() : "0");
			bool challenge = MainManager.Instance != null && MainManager.Instance.IsChallenge();
			return race + "_" + profile + (challenge ? "_challenge" : "");
		}

		private static string Path()
		{
			return System.IO.Path.Combine(Paths.ConfigPath, "MoreRelics", s_Key + ".txt");
		}

		// switching kingdom or profile re-reads, and so does starting another run
		private static void Load()
		{
			string key = Key();
			int seed = CurrentSeed();
			if (key == s_Key && (seed == 0 || seed == s_Seed))
			{
				return;
			}
			s_Key = key;
			s_Seed = seed;
			s_City = SpellType.None;
			s_Combat = SpellType.None;
			int stored = 0;
			try
			{
				string path = Path();
				if (!File.Exists(path))
				{
					return;
				}
				foreach (string line in File.ReadAllLines(path))
				{
					int split = line.IndexOf('=');
					if (split <= 0)
					{
						continue;
					}
					string name = line.Substring(split + 1).Trim();
					if (line.StartsWith("seed"))
					{
						int.TryParse(name, out stored);
						continue;
					}
					if (!Enum.IsDefined(typeof(SpellType), name))
					{
						continue;
					}
					SpellType spell = (SpellType)Enum.Parse(typeof(SpellType), name);
					if (line.StartsWith("city"))
					{
						s_City = spell;
					}
					else if (line.StartsWith("combat"))
					{
						s_Combat = spell;
					}
				}
				// another run banked these, they are not ours to hand out
				if (seed != 0 && stored != seed)
				{
					s_City = SpellType.None;
					s_Combat = SpellType.None;
					Write();
				}
			}
			catch (Exception e)
			{
				Plugin.Log?.LogError("grimoire could not read its spells: " + e);
			}
		}

		private static void Write()
		{
			try
			{
				string path = Path();
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
				File.WriteAllText(path, "city=" + s_City + "\ncombat=" + s_Combat
					+ "\nseed=" + CurrentSeed() + "\n");
			}
			catch (Exception e)
			{
				Plugin.Log?.LogError("grimoire could not write its spells: " + e);
			}
		}
	}

	// the SpellBase overloads are the acquisition path, a save restore takes the SpellType one
	[HarmonyPatch]
	internal static class Patch_GrimoireKeep
	{
		[HarmonyPatch(typeof(SpellManager), "SetCitySpell", new Type[] { typeof(SpellBase) })]
		[HarmonyPrefix]
		private static void City(SpellManager __instance, SpellBase spell)
		{
			Keep(__instance.GetCitySpell(), spell, combat: false);
		}

		[HarmonyPatch(typeof(SpellManager), "SetCombatSpell", new Type[] { typeof(SpellBase) })]
		[HarmonyPrefix]
		private static void Combat(SpellManager __instance, SpellBase spell)
		{
			Keep(__instance.GetCombatSpell(), spell, combat: true);
		}

		private static void Keep(SpellBase outgoing, SpellBase incoming, bool combat)
		{
			try
			{
				if (outgoing != null && incoming != null && outgoing != incoming && Grimoire.Active())
				{
					Reserve.Set(combat, outgoing.GetSpellType());
					Preview.Refresh();
				}
			}
			catch (Exception e)
			{
				Plugin.Log?.LogError("grimoire failed to keep the old spell: " + e);
			}
		}
	}

	// runs once per run, before any spell is picked up
	[HarmonyPatch(typeof(SpellManager), "SetDefaultSpells")]
	internal static class Patch_GrimoireReset
	{
		private static void Postfix()
		{
			try
			{
				Reserve.Clear();
				Preview.Refresh();
			}
			catch (Exception e)
			{
				Plugin.Log?.LogError("grimoire failed to clear its spells: " + e);
			}
		}
	}

	[HarmonyPatch(typeof(UISpells), "Awake")]
	internal static class Patch_GrimoireSlot
	{
		private static void Postfix(UISpells __instance)
		{
			try
			{
				Image icon = Traverse.Create(__instance).Field("icon").GetValue<Image>();
				if (icon == null)
				{
					return;
				}
				Preview.Build(icon);
				// crossing into the battlefield swaps which pair is on show
				SpellManager.OnSwitch -= OnSwitch;
				SpellManager.OnSwitch += OnSwitch;
			}
			catch (Exception e)
			{
				Plugin.Log?.LogError("grimoire could not build its slot: " + e);
			}
		}

		private static void OnSwitch(bool combat)
		{
			Preview.Refresh();
		}
	}

	// the EventSystem and the Input System both see the same click, the order is not fixed
	[HarmonyPatch(typeof(GameInputManager), "GameClick")]
	internal static class Patch_GrimoireClick
	{
		private static bool Prefix(InputAction.CallbackContext context)
		{
			try
			{
				if (!context.performed || !Preview.Hovering || !Grimoire.Active())
				{
					return true;
				}
				Grimoire.Swap();
				return false;
			}
			catch (Exception e)
			{
				Plugin.Log?.LogError("grimoire failed on click: " + e);
				return true;
			}
		}
	}
}
