using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Tavern;
using SuperFantasyKingdom.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PortraitLevels
{
	[BepInPlugin("ownly.portraitlevels", "Portrait Levels", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		private void Awake()
		{
			new Harmony(Info.Metadata.GUID).PatchAll();
			Logger.LogInfo("Portrait Levels loaded");
		}
	}

	// ------------------------------ level label ------------------------------

	internal static class LevelLabel
	{
		// position + size
		private static readonly Vector2 Anchor = new Vector2(0.77f, 0.8f);
		private static readonly Vector2 DiscSize = new Vector2(0.44f, 0.5f);
		private static readonly Vector2 TextNudge = new Vector2(0.015f, 0.02f);
		// font has a lot of padding
		private const float TextFill = 1.4f;
		private const float WideFactor = 1.6f;
		private const float Opacity = 0.41f;
		private const string OutlinedFont = "Passage7Outline";

		private static Sprite s_Disc;
		private static TMP_FontAsset s_Font;
		private static Material s_Material;

		internal static void Add(RectTransform icon, int level, Transform uiRoot)
		{
			Vector2 discHalf = DiscSize * 0.5f;
			Vector2 textHalf = DiscSize * TextFill * 0.5f;
			// wider circle
			if (level > 9)
			{
				discHalf.x *= WideFactor;
				textHalf.x *= WideFactor;
			}
			// clip mask for circle
			GameObject clipObject = new GameObject("LevelClip", typeof(RectTransform));
			RectTransform clip = (RectTransform)clipObject.transform;
			clip.SetParent(icon, false);
			clip.anchorMin = Vector2.zero;
			clip.anchorMax = Vector2.one;
			clip.offsetMin = Vector2.zero;
			clip.offsetMax = Vector2.zero;
			clipObject.AddComponent<RectMask2D>();
			// draw circle
			if (s_Disc == null)
			{
				Texture2D texture = new Texture2D(16, 16);
				texture.filterMode = FilterMode.Point;
				for (int y = 0; y < 16; y++)
				{
					for (int x = 0; x < 16; x++)
					{
						texture.SetPixel(x, y, new Color(1f, 1f, 1f, (Vector2.Distance(new Vector2(x, y), Vector2.one * 7.5f) <= 8f) ? 1f : 0f));
					}
				}
				texture.Apply();
				s_Disc = Sprite.Create(texture, new Rect(0f, 0f, 16f, 16f), Vector2.one * 0.5f);
			}
			Image disc = new GameObject("LevelDisc", typeof(RectTransform)).AddComponent<Image>();
			disc.rectTransform.SetParent(clip, false);
			disc.rectTransform.anchorMin = Anchor - discHalf;
			disc.rectTransform.anchorMax = Anchor + discHalf;
			disc.rectTransform.offsetMin = Vector2.zero;
			disc.rectTransform.offsetMax = Vector2.zero;
			disc.sprite = s_Disc;
			disc.color = new Color(0f, 0f, 0f, Opacity);
			disc.raycastTarget = false;
			// find the font
			if (s_Font == null)
			{
				TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
				s_Font = fonts.FirstOrDefault(font => font.name == OutlinedFont)
					?? fonts.FirstOrDefault(font => font.name.Contains("Outline"))
					?? uiRoot.GetComponentInChildren<TextMeshProUGUI>(true).font;
				s_Material = s_Font.material;
			}
			// level number (outside the mask)
			TextMeshProUGUI label = new GameObject("Level", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
			label.rectTransform.SetParent(icon, false);
			label.rectTransform.anchorMin = Anchor - textHalf + TextNudge;
			label.rectTransform.anchorMax = Anchor + textHalf + TextNudge;
			label.rectTransform.offsetMin = Vector2.zero;
			label.rectTransform.offsetMax = Vector2.zero;
			label.font = s_Font;
			label.fontSharedMaterial = s_Material;
			label.color = new Color32(255, 231, 0, 255);
			label.alignment = TextAlignmentOptions.Center;
			label.enableWordWrapping = false;
			label.raycastTarget = false;
			label.enableAutoSizing = true;
			label.fontSizeMin = 1f;
			label.fontSizeMax = 400f;
			label.text = level.ToString();
		}
	}

	// ------------------------------ end of run summary ------------------------------

	[HarmonyPatch(typeof(UIOverlayGameOverUnit), "Init")]
	internal static class Patch_SummaryScreen
	{
		private static void Postfix(UIOverlayGameOverUnit __instance, UnitBase unit, Image ___icon)
		{
			LevelLabel.Add(___icon.rectTransform, unit.GetLevel(), __instance.transform.root);
		}
	}

	// ------------------------------ tavern damage meter ------------------------------

	[HarmonyPatch(typeof(TavernStatistics), "GenerateDamageMeter")]
	internal static class Patch_DamageMeter
	{
		private static void Postfix(TavernStatistics __instance)
		{
			GameData.Statistics daily = TavernSaveManager.Instance.GetGameData().dailyStatistic;
			int mode = Traverse.Create(__instance).Field("m_DamageMeterMode").GetValue<int>();
			float[] meter;
			switch (mode)
			{
			case 1:
				meter = daily.damageMeter;
				break;
			case 2:
				meter = daily.healMeter;
				break;
			case 3:
				meter = daily.defenseMeter;
				break;
			default:
				meter = daily.killCount.Take(30).Select(kills => (float)kills).ToArray();
				break;
			}
			// damage meter in order
			List<UnitBase> units = Enumerable.Range(0, meter.Length)
				.OrderByDescending(identifier => meter[identifier])
				.Select(identifier => TavernUnits.Instance.GetUnit(identifier))
				.Where(unit => unit != null)
				.ToList();
			// old rows outlive the rebuild by a frame, so the new ones are the last children
			Transform content = __instance.transform.Find("DamageMeterContainer/DamageMeter/ViewPort/Content");
			int first = content.childCount - units.Count;
			for (int i = 0; i < units.Count; i++)
			{
				LevelLabel.Add(content.GetChild(first + i).Find("Icon") as RectTransform, units[i].GetLevel(), __instance.transform.root);
			}
		}
	}
}
