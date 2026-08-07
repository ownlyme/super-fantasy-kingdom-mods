using System.Linq;
using BepInEx;
using HarmonyLib;
using SuperFantasyKingdom;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiveDamageMeter
{
	[BepInPlugin("ownly.livedamagemeter", "Live Damage Meter", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		private void Awake()
		{
			new Harmony(Info.Metadata.GUID).PatchAll();
			Logger.LogInfo("Live Damage Meter loaded");
		}
	}

	// ------------------------------ attach to the game scene ------------------------------

	[HarmonyPatch(typeof(MusicManager), "Start")]
	internal static class Patch_Attach
	{
		private static void Postfix(MusicManager __instance)
		{
			__instance.gameObject.AddComponent<Meter>();
		}
	}

	// ------------------------------ live meter ------------------------------

	internal class Meter : MonoBehaviour
	{
		// panel layout, canvas units
		private const int Rows = 7;
		private const float RowHeight = 21f;
		private const float RowGap = 0f;
		private const float PanelWidth = 200f;
		private const float EdgeMargin = 14f;
		// screen fraction the panel's top edge sits at
		private const float TopEdge = 0.3f;
		private const float IconGap = 0f;
		private const float TextPad = 6f;
		private const float ValueWidth = 68f;
		private const float RefreshInterval = 0.1f;
		private const string OutlinedFont = "Passage7Outline";
		// vanilla's 7.5/10 crossfade hysteresis, stretched into a wide ellipse
		private const float EnterRange = 7.5f;
		private const float LeaveRange = 10f;
		private const float WidenX = 2.25f;
		private const float WidenY = 1.5f;
		// level disc, fractions of the icon rect
		private const float TextFill = 1.4f;
		private const float WideFactor = 1.6f;
		private const float DiscOpacity = 0.41f;
		private static readonly Vector2 DiscAnchor = new Vector2(0.77f, 0.8f);
		private static readonly Vector2 DiscSize = new Vector2(0.44f, 0.5f);
		private static readonly Vector2 TextNudge = new Vector2(0.015f, 0.02f);
		// colours
		private static readonly Color RowColor = new Color(0f, 0f, 0f, 0.78f);
		private static readonly Color BarColor = new Color(0.7f, 0.24f, 0.14f, 0.95f);
		private static readonly Color NameColor = new Color32(255, 255, 255, 255);
		private static readonly Color ValueColor = new Color32(255, 231, 0, 255);
		private static readonly Color DeadTint = new Color(1f, 1f, 1f, 0.35f);

		// autosize fits the line box, and this bitmap font's is much taller than its ink
		private const float TextHeight = RowHeight * 1.2f;

		private const float BarLeft = RowHeight + IconGap;
		private const float BarWidth = PanelWidth - BarLeft;

		private class Row
		{
			public RectTransform root;
			public RectTransform bar;
			public Image icon;
			public RectTransform disc;
			public RectTransform levelBox;
			public TextMeshProUGUI level;
			public TextMeshProUGUI unitName;
			public TextMeshProUGUI value;
		}

		private static Sprite s_Disc;
		private static TMP_FontAsset s_Font;
		private static Material s_Material;

		private AccessTools.FieldRef<MusicManager, Vector3> m_CombatLocation;
		private GameObject m_Panel;
		private Row[] m_Rows;
		private float m_Timer;
		private bool m_OnField;

		private void Awake()
		{
			// the battlefield the combat music measures its crossfade against
			try
			{
				m_CombatLocation = AccessTools.FieldRefAccess<MusicManager, Vector3>("combatLocation");
			}
			catch
			{
				enabled = false;
			}
		}

		private void Update()
		{
			try
			{
				if (MusicManager.Instance == null || CombatManager.Instance == null || GameCamera.Instance == null)
				{
					return;
				}
				// vanilla freezes its own copy of this outside combat, ours runs every frame
				Vector3 drift = m_CombatLocation(MusicManager.Instance) - GameCamera.Instance.transform.position;
				// squashing the axes is the same as stretching the ranges
				float distance = new Vector3(drift.x / WidenX, drift.y / WidenY, drift.z).magnitude;
				if (distance < EnterRange)
				{
					m_OnField = true;
				}
				else if (distance > LeaveRange)
				{
					m_OnField = false;
				}
				bool show = m_OnField && (CombatManager.Instance.IsCombat() || CombatManager.Instance.IsCombatOver());
				if (show && m_Panel == null)
				{
					Build();
				}
				if (m_Panel == null)
				{
					return;
				}
				if (m_Panel.activeSelf != show)
				{
					m_Panel.SetActive(show);
				}
				if (!show)
				{
					return;
				}
				m_Timer -= Time.unscaledDeltaTime;
				if (m_Timer > 0f)
				{
					return;
				}
				m_Timer = RefreshInterval;
				Refresh();
			}
			catch
			{
				// never spam the log or stall the frame
				if (m_Panel != null)
				{
					m_Panel.SetActive(value: false);
				}
				enabled = false;
			}
		}

		private void Build()
		{
			// the hud canvas is the busiest screen space one
			Canvas canvas = FindObjectsOfType<Canvas>()
				.Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
				.OrderByDescending(c => c.transform.childCount)
				.FirstOrDefault();
			if (canvas == null)
			{
				return;
			}
			// shared circle texture
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
			// the outline is baked into the font asset, not the material
			if (s_Font == null)
			{
				TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
				s_Font = fonts.FirstOrDefault(font => font.name == OutlinedFont)
					?? fonts.FirstOrDefault(font => font.name.Contains("Outline"))
					?? canvas.GetComponentInChildren<TextMeshProUGUI>(true).font;
				s_Material = s_Font.material;
			}
			// left edge, top of the panel at TopEdge, growing downward
			float panelHeight = Rows * (RowHeight + RowGap) - RowGap;
			m_Panel = new GameObject("LiveDamageMeter", typeof(RectTransform));
			RectTransform panel = (RectTransform)m_Panel.transform;
			panel.SetParent(canvas.transform, false);
			panel.SetAsFirstSibling();
			panel.anchorMin = new Vector2(0f, TopEdge);
			panel.anchorMax = new Vector2(0f, TopEdge);
			panel.pivot = new Vector2(0f, 1f);
			panel.sizeDelta = new Vector2(PanelWidth, panelHeight);
			panel.anchoredPosition = new Vector2(EdgeMargin, 0f);
			m_Rows = new Row[Rows];
			for (int i = 0; i < Rows; i++)
			{
				Row row = new Row();
				m_Rows[i] = row;
				// row slot
				row.root = (RectTransform)new GameObject("Row" + i, typeof(RectTransform)).transform;
				row.root.SetParent(panel, false);
				row.root.anchorMin = new Vector2(0f, 1f);
				row.root.anchorMax = new Vector2(0f, 1f);
				row.root.pivot = new Vector2(0f, 1f);
				row.root.sizeDelta = new Vector2(PanelWidth, RowHeight);
				row.root.anchoredPosition = new Vector2(0f, (0f - (float)i) * (RowHeight + RowGap));
				row.root.gameObject.SetActive(value: false);
				// row backdrop
				Image backdrop = new GameObject("Backdrop", typeof(RectTransform)).AddComponent<Image>();
				backdrop.rectTransform.SetParent(row.root, false);
				backdrop.rectTransform.anchorMin = Vector2.zero;
				backdrop.rectTransform.anchorMax = Vector2.one;
				backdrop.rectTransform.offsetMin = Vector2.zero;
				backdrop.rectTransform.offsetMax = Vector2.zero;
				backdrop.color = RowColor;
				backdrop.raycastTarget = false;
				// damage bar
				Image bar = new GameObject("Bar", typeof(RectTransform)).AddComponent<Image>();
				row.bar = bar.rectTransform;
				row.bar.SetParent(row.root, false);
				row.bar.anchorMin = new Vector2(0f, 0.5f);
				row.bar.anchorMax = new Vector2(0f, 0.5f);
				row.bar.pivot = new Vector2(0f, 0.5f);
				row.bar.anchoredPosition = new Vector2(BarLeft, 0f);
				bar.color = BarColor;
				bar.raycastTarget = false;
				// unit portrait
				row.icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
				row.icon.rectTransform.SetParent(row.root, false);
				row.icon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
				row.icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
				row.icon.rectTransform.pivot = new Vector2(0f, 0.5f);
				row.icon.rectTransform.anchoredPosition = Vector2.zero;
				row.icon.rectTransform.sizeDelta = new Vector2(RowHeight, RowHeight);
				row.icon.preserveAspect = true;
				row.icon.raycastTarget = false;
				// clip mask keeps the disc inside the portrait, never put it on the icon itself
				RectTransform clip = (RectTransform)new GameObject("LevelClip", typeof(RectTransform)).transform;
				clip.SetParent(row.icon.rectTransform, false);
				clip.anchorMin = Vector2.zero;
				clip.anchorMax = Vector2.one;
				clip.offsetMin = Vector2.zero;
				clip.offsetMax = Vector2.zero;
				clip.gameObject.AddComponent<RectMask2D>();
				// level disc
				Image disc = new GameObject("LevelDisc", typeof(RectTransform)).AddComponent<Image>();
				row.disc = disc.rectTransform;
				row.disc.SetParent(clip, false);
				row.disc.offsetMin = Vector2.zero;
				row.disc.offsetMax = Vector2.zero;
				disc.sprite = s_Disc;
				disc.color = new Color(0f, 0f, 0f, DiscOpacity);
				disc.raycastTarget = false;
				// level number, outside the mask
				row.level = new GameObject("Level", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
				row.levelBox = row.level.rectTransform;
				row.levelBox.SetParent(row.icon.rectTransform, false);
				row.levelBox.offsetMin = Vector2.zero;
				row.levelBox.offsetMax = Vector2.zero;
				row.level.font = s_Font;
				row.level.fontSharedMaterial = s_Material;
				row.level.color = ValueColor;
				row.level.alignment = TextAlignmentOptions.Center;
				row.level.enableWordWrapping = false;
				row.level.raycastTarget = false;
				row.level.enableAutoSizing = true;
				row.level.fontSizeMin = 1f;
				row.level.fontSizeMax = 400f;
				// unit name
				row.unitName = new GameObject("Name", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
				row.unitName.rectTransform.SetParent(row.root, false);
				row.unitName.rectTransform.anchorMin = new Vector2(0f, 0.5f);
				row.unitName.rectTransform.anchorMax = new Vector2(0f, 0.5f);
				row.unitName.rectTransform.pivot = new Vector2(0f, 0.5f);
				row.unitName.rectTransform.anchoredPosition = new Vector2(BarLeft + TextPad, 0f);
				row.unitName.rectTransform.sizeDelta = new Vector2(BarWidth - TextPad * 2f - ValueWidth, TextHeight);
				row.unitName.font = s_Font;
				row.unitName.fontSharedMaterial = s_Material;
				row.unitName.color = NameColor;
				row.unitName.alignment = TextAlignmentOptions.Left;
				row.unitName.enableWordWrapping = false;
				row.unitName.overflowMode = TextOverflowModes.Ellipsis;
				row.unitName.raycastTarget = false;
				row.unitName.enableAutoSizing = true;
				row.unitName.fontSizeMin = 8f;
				row.unitName.fontSizeMax = 21f;
				// damage value
				row.value = new GameObject("Value", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
				row.value.rectTransform.SetParent(row.root, false);
				row.value.rectTransform.anchorMin = new Vector2(0f, 0.5f);
				row.value.rectTransform.anchorMax = new Vector2(0f, 0.5f);
				row.value.rectTransform.pivot = new Vector2(0f, 0.5f);
				row.value.rectTransform.anchoredPosition = new Vector2(PanelWidth - TextPad - ValueWidth, 0f);
				row.value.rectTransform.sizeDelta = new Vector2(ValueWidth, TextHeight);
				row.value.font = s_Font;
				row.value.fontSharedMaterial = s_Material;
				row.value.color = ValueColor;
				row.value.alignment = TextAlignmentOptions.Right;
				row.value.enableWordWrapping = false;
				row.value.raycastTarget = false;
				row.value.enableAutoSizing = true;
				row.value.fontSizeMin = 8f;
				row.value.fontSizeMax = 21f;
			}
		}

		private void Refresh()
		{
			// live totals, the same arrays the tavern meter is later built from
			float[] meter = StatisticsManager.Instance.GetDamageMeter();
			int[] order = Enumerable.Range(0, meter.Length)
				.Where(identifier => meter[identifier] > 0f && UnitManager.Instance.GetUnit(identifier) != null)
				.OrderByDescending(identifier => meter[identifier])
				.Take(Rows)
				.ToArray();
			float top = (order.Length > 0) ? meter[order[0]] : 1f;
			for (int rank = 0; rank < Rows; rank++)
			{
				Row row = m_Rows[rank];
				if (rank >= order.Length)
				{
					row.root.gameObject.SetActive(value: false);
					continue;
				}
				float damage = meter[order[rank]];
				UnitBase unit = UnitManager.Instance.GetUnit(order[rank]);
				row.root.gameObject.SetActive(value: true);
				row.bar.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(damage / top), RowHeight);
				row.icon.sprite = unit.GetIcon();
				row.icon.color = unit.IsDead() ? DeadTint : Color.white;
				row.unitName.text = unit.GetName();
				row.value.text = (damage >= 10000f) ? ((damage / 1000f).ToString("F1") + "k") : Mathf.RoundToInt(damage).ToString("N0");
				// two digit levels widen the disc symmetrically so the number never drifts
				int level = unit.GetLevel();
				Vector2 discHalf = DiscSize * 0.5f;
				Vector2 textHalf = DiscSize * TextFill * 0.5f;
				if (level > 9)
				{
					discHalf.x *= WideFactor;
					textHalf.x *= WideFactor;
				}
				row.disc.anchorMin = DiscAnchor - discHalf;
				row.disc.anchorMax = DiscAnchor + discHalf;
				row.levelBox.anchorMin = DiscAnchor - textHalf + TextNudge;
				row.levelBox.anchorMax = DiscAnchor + textHalf + TextNudge;
				row.level.text = level.ToString();
			}
		}
	}
}
