using UnityEngine;
using UnityEngine.UI;

namespace KingdomWatch.Game
{
    public enum TextAlign
    {
        Left,
        Centre,
        Right,
    }

    // Text drawn from the pack's 5x7 pixel font (#128): one quad a glyph,
    // laid out in art pixels, so it is crisp at the integer scale the panel
    // runs at and unaffected by dynamic font rasterising. One line only.
    public sealed class BitmapText : MaskableGraphic
    {
        private HudArt art;
        private string value = "";
        private TextAlign align;

        public override Texture mainTexture => art != null ? art.FontTexture : null;

        public void Setup(HudArt hudArt, TextAlign textAlign)
        {
            art = hudArt;
            align = textAlign;
            raycastTarget = false;
        }

        public string Value
        {
            get => value;
            set
            {
                if (this.value == value) return;
                this.value = value;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            if (art == null || string.IsNullOrEmpty(value)) return;

            var area = rectTransform.rect;
            var width = art.WidthOf(value);
            float x;
            switch (align)
            {
                case TextAlign.Centre: x = Mathf.Round(area.center.x - width / 2f); break;
                case TextAlign.Right: x = area.xMax - width; break;
                default: x = area.xMin; break;
            }
            // The line's top is the rect's top; the baseline is GlyphAscent below.
            var top = Mathf.Round(area.yMax);
            var index = 0;
            var pen = x;
            for (var i = 0; i < value.Length; i++)
            {
                var advance = HudArt.GlyphAdvance;
                if (art.TryGlyph(value[i], out var glyph))
                {
                    advance = glyph.Advance;
                }
                if (glyph.Height > 0)
                {
                    var left = pen;
                    var uv = glyph.Uv;
                    vertices.AddVert(new Vector3(left, top - glyph.Height), color, new Vector2(uv.xMin, uv.yMin));
                    vertices.AddVert(new Vector3(left, top), color, new Vector2(uv.xMin, uv.yMax));
                    vertices.AddVert(new Vector3(left + HudArt.GlyphAdvance, top), color, new Vector2(uv.xMax, uv.yMax));
                    vertices.AddVert(new Vector3(left + HudArt.GlyphAdvance, top - glyph.Height), color, new Vector2(uv.xMax, uv.yMin));
                    vertices.AddTriangle(index, index + 1, index + 2);
                    vertices.AddTriangle(index + 2, index + 3, index);
                    index += 4;
                }
                pen += advance;
            }
        }
    }

    // One line of panel text: the pixel font when the art is there, and
    // Unity's own font otherwise.
    public sealed class Label
    {
        private readonly BitmapText bitmap;
        private readonly Text plain;

        public Label(Transform parent, HudArt art, TextAlign align, Color colour)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            Rect = (RectTransform)go.transform;
            Rect.SetParent(parent, false);
            Rect.anchorMin = Rect.anchorMax = new Vector2(0f, 1f);
            Rect.pivot = new Vector2(0f, 1f);
            if (art.FontTexture != null)
            {
                bitmap = go.AddComponent<BitmapText>();
                bitmap.Setup(art, align);
                bitmap.color = colour;
            }
            else
            {
                plain = go.AddComponent<Text>();
                plain.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                plain.fontSize = 8;
                plain.color = colour;
                plain.raycastTarget = false;
                plain.horizontalOverflow = HorizontalWrapMode.Overflow;
                plain.alignment = align == TextAlign.Left ? TextAnchor.UpperLeft : align == TextAlign.Right ? TextAnchor.UpperRight : TextAnchor.UpperCenter;
            }
        }

        public RectTransform Rect { get; }

        public GameObject Object => Rect.gameObject;

        public string Value
        {
            get => bitmap != null ? bitmap.Value : plain.text;
            set
            {
                if (bitmap != null) bitmap.Value = value;
                else if (plain.text != value) plain.text = value;
            }
        }

        // Where the line sits inside its parent, in art pixels from the top left.
        public void Place(float x, float y, float width)
        {
            Rect.anchoredPosition = new Vector2(x, -y);
            Rect.sizeDelta = new Vector2(width, HudArt.LineHeight);
        }
    }
}
