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
		private const float PanelWidth = 200f;
		private const float EdgeMargin = 14f;
		// screen fraction the panel's top edge sits at
		private const float TopEdge = 0.3f;
		private const float TextPad = 6f;
		private const float ValueWidth = 68f;
		private const float RefreshInterval = 0.1f;
		private const string OutlinedFont = "Passage7Outline";
		// black frame: 2 canvas units reads as 4 px at the usual 2x ui scale
		private const float Border = 2f;
		private const float Gap = 2f;
		// corner left unpainted this far along both sides
		private const float CornerCut = 2f;
		// vanilla's 7.5/10 crossfade hysteresis, stretched into a wide ellipse
		private const float EnterRange = 7.5f;
		private const float LeaveRange = 10f;
		private const float WidenX = 2.25f;
		private const float WidenY = 1.5f;
		// level disc, fractions of the icon rect
		private const float TextFill = 1.4f;
		private const float WideFactor = 1.6f;
		private const float DiscOpacity = 0.41f;
		private static readonly Vector2 DiscAnchor = new Vector2(0.78f, 0.79f);
		private static readonly Vector2 DiscSize = new Vector2(0.5f, 0.56f);
		private static readonly Vector2 TextNudge = new Vector2(0.015f, 0.02f);
		// colours
		private static readonly Color FrameColor = new Color(0f, 0f, 0f, 1f);
		private static readonly Color RowColor = new Color(0f, 0f, 0f, 0.78f);
		private static readonly Color BarColor = new Color(0.7f, 0.24f, 0.14f, 0.95f);
		private static readonly Color NameColor = new Color32(255, 255, 255, 255);
		private static readonly Color ValueColor = new Color32(255, 231, 0, 255);
		private static readonly Color DeadTint = new Color(1f, 1f, 1f, 0.35f);

		// autosize fits the line box, and this bitmap font's is much taller than its ink
		private const float TextHeight = RowHeight * 1.2f;

		// derived: portrait, divider, then the bar, all inside the frame
		private const float InnerWidth = PanelWidth - Border * 2f;
		private const float BarLeft = RowHeight + Gap;
		private const float BarWidth = InnerWidth - BarLeft;
		private const float RowPitch = RowHeight + Gap;

		// one row per unit type at a given level, so three trebuchets share a slot
		private class Group
		{
			public UnitBase unit;
			public float damage;
			public int count;
			public bool allDead;
		}

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
		private RectTransform m_PanelRect;
		private Row[] m_Rows;
		private Group[] m_Groups;
		private GameObject[] m_Dividers;
		private float m_Timer;
		private bool m_OnField;
		private int m_Shown;

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
				if (!(m_OnField && (CombatManager.Instance.IsCombat() || CombatManager.Instance.IsCombatOver())))
				{
					if (m_Panel != null && m_Panel.activeSelf)
					{
						m_Panel.SetActive(value: false);
					}
					// refresh on the first visible frame, not on a stale timer
					m_Timer = 0f;
					return;
				}
				if (m_Panel == null)
				{
					Build();
				}
				if (m_Panel == null)
				{
					return;
				}
				m_Timer -= Time.unscaledDeltaTime;
				if (m_Timer <= 0f)
				{
					m_Timer = RefreshInterval;
					Refresh();
				}
				// an empty frame before the first hit reads as a bug
				if (m_Panel.activeSelf != (m_Shown > 0))
				{
					m_Panel.SetActive(m_Shown > 0);
				}
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
			// panel, hanging down from TopEdge. Refresh sizes the height
			m_Panel = new GameObject("LiveDamageMeter", typeof(RectTransform));
			m_PanelRect = (RectTransform)m_Panel.transform;
			m_PanelRect.SetParent(canvas.transform, false);
			m_PanelRect.SetAsFirstSibling();
			m_PanelRect.anchorMin = new Vector2(0f, TopEdge);
			m_PanelRect.anchorMax = new Vector2(0f, TopEdge);
			m_PanelRect.pivot = new Vector2(0f, 1f);
			m_PanelRect.sizeDelta = new Vector2(PanelWidth, Border * 2f + Rows * RowPitch - Gap);
			m_PanelRect.anchoredPosition = new Vector2(EdgeMargin, 0f);
			m_Rows = new Row[Rows];
			for (int i = 0; i < Rows; i++)
			{
				Row row = new Row();
				m_Rows[i] = row;
				// row slot, inset by the frame
				row.root = (RectTransform)new GameObject("Row" + i, typeof(RectTransform)).transform;
				row.root.SetParent(m_PanelRect, false);
				row.root.anchorMin = new Vector2(0f, 1f);
				row.root.anchorMax = new Vector2(0f, 1f);
				row.root.pivot = new Vector2(0f, 1f);
				row.root.sizeDelta = new Vector2(InnerWidth, RowHeight);
				row.root.anchoredPosition = new Vector2(Border, 0f - Border - (float)i * RowPitch);
				row.root.gameObject.SetActive(value: false);
				// row backdrop
				Image backdrop = MakeImage(row.root, "Backdrop", RowColor);
				backdrop.rectTransform.anchorMin = Vector2.zero;
				backdrop.rectTransform.anchorMax = Vector2.one;
				backdrop.rectTransform.offsetMin = Vector2.zero;
				backdrop.rectTransform.offsetMax = Vector2.zero;
				// damage bar
				Image bar = MakeImage(row.root, "Bar", BarColor);
				row.bar = bar.rectTransform;
				row.bar.anchorMin = new Vector2(0f, 0.5f);
				row.bar.anchorMax = new Vector2(0f, 0.5f);
				row.bar.pivot = new Vector2(0f, 0.5f);
				row.bar.anchoredPosition = new Vector2(BarLeft, 0f);
				// unit portrait
				row.icon = MakeImage(row.root, "Icon", Color.white);
				row.icon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
				row.icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
				row.icon.rectTransform.pivot = new Vector2(0f, 0.5f);
				row.icon.rectTransform.anchoredPosition = Vector2.zero;
				row.icon.rectTransform.sizeDelta = new Vector2(RowHeight, RowHeight);
				row.icon.preserveAspect = true;
				// clip mask keeps the disc inside the portrait, never put it on the icon itself
				RectTransform clip = (RectTransform)new GameObject("LevelClip", typeof(RectTransform)).transform;
				clip.SetParent(row.icon.rectTransform, false);
				clip.anchorMin = Vector2.zero;
				clip.anchorMax = Vector2.one;
				clip.offsetMin = Vector2.zero;
				clip.offsetMax = Vector2.zero;
				clip.gameObject.AddComponent<RectMask2D>();
				// level disc
				Image disc = MakeImage(clip, "LevelDisc", new Color(0f, 0f, 0f, DiscOpacity));
				row.disc = disc.rectTransform;
				row.disc.offsetMin = Vector2.zero;
				row.disc.offsetMax = Vector2.zero;
				disc.sprite = s_Disc;
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
				row.value.rectTransform.anchoredPosition = new Vector2(InnerWidth - TextPad - ValueWidth, 0f);
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
			// frame sides, last so they paint over the row backdrops
			Image top = MakeImage(m_PanelRect, "FrameTop", FrameColor);
			top.rectTransform.anchorMin = new Vector2(0f, 1f);
			top.rectTransform.anchorMax = new Vector2(1f, 1f);
			top.rectTransform.offsetMin = new Vector2(CornerCut, 0f - Border);
			top.rectTransform.offsetMax = new Vector2(0f - CornerCut, 0f);
			Image bottom = MakeImage(m_PanelRect, "FrameBottom", FrameColor);
			bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
			bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
			bottom.rectTransform.offsetMin = new Vector2(CornerCut, 0f);
			bottom.rectTransform.offsetMax = new Vector2(0f - CornerCut, Border);
			Image left = MakeImage(m_PanelRect, "FrameLeft", FrameColor);
			left.rectTransform.anchorMin = new Vector2(0f, 0f);
			left.rectTransform.anchorMax = new Vector2(0f, 1f);
			left.rectTransform.offsetMin = new Vector2(0f, CornerCut);
			left.rectTransform.offsetMax = new Vector2(Border, 0f - CornerCut);
			Image right = MakeImage(m_PanelRect, "FrameRight", FrameColor);
			right.rectTransform.anchorMin = new Vector2(1f, 0f);
			right.rectTransform.anchorMax = new Vector2(1f, 1f);
			right.rectTransform.offsetMin = new Vector2(0f - Border, CornerCut);
			right.rectTransform.offsetMax = new Vector2(0f, 0f - CornerCut);
			// portrait divider, one column down the panel
			Image column = MakeImage(m_PanelRect, "DividerColumn", FrameColor);
			column.rectTransform.anchorMin = new Vector2(0f, 0f);
			column.rectTransform.anchorMax = new Vector2(0f, 1f);
			column.rectTransform.offsetMin = new Vector2(Border + RowHeight, Border);
			column.rectTransform.offsetMax = new Vector2(Border + RowHeight + Gap, 0f - Border);
			// row dividers, pinned to the top edge so a resize never moves them
			m_Dividers = new GameObject[Rows - 1];
			for (int j = 0; j < Rows - 1; j++)
			{
				Image divider = MakeImage(m_PanelRect, "Divider" + j, FrameColor);
				m_Dividers[j] = divider.gameObject;
				divider.rectTransform.anchorMin = new Vector2(0f, 1f);
				divider.rectTransform.anchorMax = new Vector2(1f, 1f);
				divider.rectTransform.pivot = new Vector2(0.5f, 1f);
				float below = Border + (float)(j + 1) * RowPitch - Gap;
				divider.rectTransform.offsetMin = new Vector2(Border, 0f - below - Gap);
				divider.rectTransform.offsetMax = new Vector2(0f - Border, 0f - below);
				divider.gameObject.SetActive(value: false);
			}
			m_Panel.SetActive(value: false);
		}

		// flat colour child that must not eat clicks on the battlefield
		private static Image MakeImage(Transform parent, string name, Color color)
		{
			Image image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
			image.rectTransform.SetParent(parent, false);
			image.color = color;
			image.raycastTarget = false;
			return image;
		}

		private void Refresh()
		{
			// live totals, the same arrays the tavern meter is later built from
			float[] meter = StatisticsManager.Instance.GetDamageMeter();
			if (m_Groups == null || m_Groups.Length < meter.Length)
			{
				m_Groups = new Group[meter.Length];
				for (int g = 0; g < m_Groups.Length; g++)
				{
					m_Groups[g] = new Group();
				}
			}
			// fold identical units together: same type, same evolution stage, same level.
			// GetEntityIdentifier is the type key the rename system uses, not the display name
			int total = 0;
			for (int identifier = 0; identifier < meter.Length; identifier++)
			{
				if (meter[identifier] <= 0f)
				{
					continue;
				}
				UnitBase unit = UnitManager.Instance.GetUnit(identifier);
				if (unit == null)
				{
					continue;
				}
				int slot = -1;
				for (int g = 0; g < total; g++)
				{
					UnitBase other = m_Groups[g].unit;
					if (other.GetLevel() == unit.GetLevel() && other.IsEvolved() == unit.IsEvolved() && other.GetEntityIdentifier() == unit.GetEntityIdentifier())
					{
						slot = g;
						break;
					}
				}
				if (slot < 0)
				{
					slot = total++;
					m_Groups[slot].unit = unit;
					m_Groups[slot].damage = 0f;
					m_Groups[slot].count = 0;
					m_Groups[slot].allDead = true;
				}
				m_Groups[slot].damage += meter[identifier];
				m_Groups[slot].count++;
				// one survivor keeps the whole row at full colour
				if (!unit.IsDead())
				{
					m_Groups[slot].allDead = false;
				}
			}
			m_Shown = Mathf.Min(total, Rows);
			// only the visible rows need ordering, so rank by partial selection
			for (int rank = 0; rank < m_Shown; rank++)
			{
				int best = rank;
				for (int g = rank + 1; g < total; g++)
				{
					if (m_Groups[g].damage > m_Groups[best].damage)
					{
						best = g;
					}
				}
				Group swap = m_Groups[rank];
				m_Groups[rank] = m_Groups[best];
				m_Groups[best] = swap;
			}
			// frame hugs the contributor count
			if (m_Shown > 0)
			{
				m_PanelRect.sizeDelta = new Vector2(PanelWidth, Border * 2f + (float)m_Shown * RowPitch - Gap);
			}
			for (int j = 0; j < m_Dividers.Length; j++)
			{
				bool between = j < m_Shown - 1;
				if (m_Dividers[j].activeSelf != between)
				{
					m_Dividers[j].SetActive(between);
				}
			}
			float top = (m_Shown > 0) ? m_Groups[0].damage : 1f;
			for (int rank = 0; rank < Rows; rank++)
			{
				Row row = m_Rows[rank];
				if (rank >= m_Shown)
				{
					row.root.gameObject.SetActive(value: false);
					continue;
				}
				Group group = m_Groups[rank];
				float damage = group.damage;
				UnitBase unit = group.unit;
				row.root.gameObject.SetActive(value: true);
				row.bar.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(damage / top), RowHeight);
				row.icon.sprite = unit.GetIcon();
				row.icon.color = group.allDead ? DeadTint : Color.white;
				row.unitName.text = (group.count > 1) ? (unit.GetName() + " x" + group.count) : unit.GetName();
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
