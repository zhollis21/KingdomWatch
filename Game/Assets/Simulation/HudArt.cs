using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomWatch.Game
{
    // The panel's pieces (#128), cut at runtime from whole sheets under
    // Resources/ the way ArtSet cuts the map's: Update-Resources.ps1 in the
    // art repository copies exactly these five files. Every rectangle is in
    // sheet pixels, counted from the top left as an image editor shows them.
    // Load returns null when the art submodule is missing, and the panel then
    // draws flat frames in Unity's own font.
    public sealed class HudArt
    {
        private const string Ui = "Cute_Fantasy_UI/UI/";
        private const string Icons = "Cute_Fantasy/Icons/Outline/";

        // The frame tiles in UI_Frames sit on a 48 px grid; each is 28x31
        // with its shadow, starting at (10, 10). Row 0 is tan, row 1 grey.
        private const int FrameWidth = 28, FrameHeight = 31, FrameBorder = 7;

        // The pixel font: 27 glyphs of 5 px across (4 and a gap) in each of
        // three bands. Every glyph's baseline is 5 px below its band's top,
        // so a line is laid out from one baseline.
        public const int GlyphAdvance = 5;
        public const int GlyphAscent = 5;
        public const int LineHeight = 9;
        private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string Lower = "abcdefghijklmnopqrstuvwxyz";
        private const string Symbols = "1234567890!?()/\\><%.,;:+-=x";

        public Sprite Panel { get; private set; }
        public Sprite Button { get; private set; }
        public Sprite Pause { get; private set; }
        public Sprite Play { get; private set; }
        public Sprite Wood { get; private set; }
        public Sprite Stone { get; private set; }
        public Sprite Berries { get; private set; }

        // A villager's head, in two layers: the skin, then the hair over it.
        // Null when the villager sheets are missing.
        public Sprite HeadSkin { get; private set; }
        public Sprite HeadHair { get; private set; }

        // One opaque pixel: bars, dots and the minimap's outline.
        public Sprite White { get; private set; }

        public Texture2D FontTexture { get; private set; }

        private readonly Dictionary<char, Glyph> glyphs = new Dictionary<char, Glyph>();

        public readonly struct Glyph
        {
            public readonly Rect Uv;
            public readonly int Height;

            // Pixels from this glyph to the next: the cell, and a gap after the
            // few glyphs that fill all five columns of it.
            public readonly int Advance;

            public Glyph(Rect uv, int height, int advance)
            {
                Uv = uv;
                Height = height;
                Advance = advance;
            }
        }

        // Glyphs that use every column of their cell.
        private const string Wide = "MWmw";

        public bool TryGlyph(char c, out Glyph glyph) => glyphs.TryGetValue(c, out glyph);

        // A line's width in pixels, without the gap after its last glyph.
        public int WidthOf(string text)
        {
            var width = 0;
            for (var i = 0; i < text.Length; i++) width += glyphs.TryGetValue(text[i], out var glyph) ? glyph.Advance : GlyphAdvance;
            return Mathf.Max(0, width - 1);
        }

        private HudArt()
        {
        }

        // The sprites made flat, for a build without the art: frames are a
        // 9-sliced block of colour, and there is no font or icon.
        public static HudArt Flat(List<Object> owned)
        {
            var art = new HudArt();
            art.White = White1(owned);
            art.Panel = art.Button = Sprite.Create(art.White.texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            owned.Add(art.Panel);
            return art;
        }

        public static HudArt Load(List<Object> owned)
        {
            var frames = Resources.Load<Texture2D>(Ui + "UI_Frames");
            var font = Resources.Load<Texture2D>(Ui + "Cute_Fantasy_Font_5x7");
            var buttonIcons = Resources.Load<Texture2D>(Ui + "UI_Button_Icons");
            var resources = Resources.Load<Texture2D>(Icons + "Resources_Icons_Outline");
            var food = Resources.Load<Texture2D>(Icons + "Food_Icons_Outline");
            if (frames == null || font == null || buttonIcons == null || resources == null || food == null) return null;

            var art = new HudArt { FontTexture = font };
            art.White = White1(owned);
            art.Panel = Framed(frames, 0, owned);
            art.Button = Framed(frames, 1, owned);
            // Row 3 of the button icons is the dark slate set, which shows on the grey buttons.
            art.Pause = Cell(buttonIcons, 0, 48, owned);
            art.Play = Cell(buttonIcons, 16, 48, owned);
            art.Wood = Cell(resources, 0, 64, owned);
            art.Stone = Cell(resources, 0, 80, owned);
            // The cherries stand in for berries.
            art.Berries = Cell(food, 96, 96, owned);
            var skin = Resources.Load<Texture2D>("Cute_Fantasy/Player/Player_Base/Player_Base_animations");
            var hair = Resources.Load<Texture2D>("Cute_Fantasy/Player/Head/Hair_1/Hair_1_Brown");
            if (skin != null && hair != null)
            {
                // The first frame of each sheet (64 px, facing down): the head sits near its middle.
                art.HeadSkin = Head(skin, owned);
                art.HeadHair = Head(hair, owned);
            }
            art.CutFont(font);
            return art;
        }

        private static Sprite White1(List<Object> owned)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            owned.Add(texture);
            owned.Add(sprite);
            return sprite;
        }

        private static Sprite Framed(Texture2D sheet, int row, List<Object> owned)
        {
            var top = 10 + 48 * row;
            var rect = new Rect(10f, sheet.height - top - FrameHeight, FrameWidth, FrameHeight);
            var sprite = Sprite.Create(sheet, rect, new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect,
                new Vector4(FrameBorder, FrameBorder, FrameBorder, FrameBorder));
            owned.Add(sprite);
            return sprite;
        }

        private static Sprite Head(Texture2D sheet, List<Object> owned)
        {
            var sprite = Sprite.Create(sheet, new Rect(24f, sheet.height - 18 - 16, 16f, 16f), new Vector2(0.5f, 0.5f), 1f);
            owned.Add(sprite);
            return sprite;
        }

        private static Sprite Cell(Texture2D sheet, int x, int top, List<Object> owned)
        {
            var sprite = Sprite.Create(sheet, new Rect(x, sheet.height - top - 16, 16, 16), new Vector2(0.5f, 0.5f), 1f);
            owned.Add(sprite);
            return sprite;
        }

        // Band tops and heights in the font sheet: capitals, then lower case
        // (taller, for the descenders), then digits and symbols.
        private void CutFont(Texture2D font)
        {
            CutBand(font, Upper, 2, 5);
            CutBand(font, Lower, 9, 7);
            CutBand(font, Symbols, 23, 6);
            glyphs[' '] = new Glyph(new Rect(0f, 0f, 0f, 0f), 0, GlyphAdvance);
        }

        private void CutBand(Texture2D font, string characters, int top, int height)
        {
            for (var i = 0; i < characters.Length; i++)
            {
                var uv = new Rect(
                    GlyphAdvance * i / (float)font.width,
                    (font.height - top - height) / (float)font.height,
                    GlyphAdvance / (float)font.width,
                    height / (float)font.height);
                glyphs[characters[i]] = new Glyph(uv, height, Wide.IndexOf(characters[i]) >= 0 ? GlyphAdvance + 1 : GlyphAdvance);
            }
        }
    }
}
