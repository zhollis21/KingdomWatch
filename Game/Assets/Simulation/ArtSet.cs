using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace KingdomWatch.Game
{
    // Kenmi's Cute Fantasy art (#121), read from the private art submodule at
    // Assets/Art. The packs may not be redistributed and this repository is
    // public, so a clone without the submodule has none of it: Load returns
    // null and WorldView2D draws its plain markers instead.
    public sealed class ArtSet
    {
        // Art pixels per map cell: one 16 px tile is one cell, about 1.5 m,
        // and a villager is about one cell tall.
        public const int PixelsPerCell = 16;

        // A villager's drawn size in cells, for picking and the selection box.
        public const float FigureWidth = 14f / PixelsPerCell;
        public const float FigureHeight = 20f / PixelsPerCell;

        // The paper doll, bottom to top. Each sheet shares one frame layout,
        // so any shirt fits any body.
        public const int Layers = 6;

        public const int FramesPerPose = 6;

        // Resources/<pack>/..., as Update-Resources.ps1 in the art repository
        // lays them out.
        private const string Pack = "Cute_Fantasy/";
        private const string Player = Pack + "Player/";
        private const string Decor = Pack + "Outdoor decoration/";

        // Villager sheets are 64 px frames with the figure in the middle;
        // only this 32 px square of each is ever drawn, and only the rows
        // Update-Resources.ps1 in the art repository keeps: a Motion's three
        // in turn, each facing down, side and up.
        private const int SheetFrame = 64;
        private const int CropLeft = 16, CropTop = 12, Crop = 32;
        // The feet sit 3 px above the crop's bottom edge.
        private static readonly Vector2 FeetPivot = new Vector2(0.5f, 3f / Crop);
        private const int Motions = 5;
        private const int Poses = Motions * 3;

        // A tool swings well outside the figure's square, so tools are cut
        // wider, from the same 64 px frames: this 40x44 box holds every
        // tool's pixels, centred on the figure's, with the feet at the same
        // height (41 px down the frame) as the doll's.
        private const int ToolLeft = 12, ToolTop = 8, ToolWidth = 40, ToolHeight = 44;
        private static readonly Vector2 ToolPivot = new Vector2(0.5f, (float)(ToolTop + ToolHeight - (CropTop + Crop - 3)) / ToolHeight);

        // Each Tool's sheet and first row, in Tool order after None: the
        // pack's sheet holds four tools, three rows each; the hammer and the
        // sickle were drawn for the art repository over its hoe (#126).
        private static readonly (string Sheet, int Row)[] ToolSheets =
        {
            (Player + "Tools/Iron/Iron_Tools", 0),
            (Player + "Tools/Iron/Iron_Tools", 3),
            (Player + "Tools/Iron/Iron_Tools", 6),
            (Player + "Tools/Iron/Iron_Tools", 9),
            (Player + "Tools/Iron/Iron_Hammer", 0),
            (Player + "Tools/Iron/Iron_Sickle", 0),
        };

        private static readonly string[] HairColours = { "Black", "Blonde", "Brown", "Ginger" };
        private static readonly string[] PantsColours = { "Brown", "Blue", "Green", "Black" };
        private static readonly string[] ShoeColours = { "Brown", "Black" };

        // The ground palette for each season, in Season order: spring's bright
        // green, summer's deeper green, autumn's yellow-green, winter's snow.
        // Each sheet has the same layout, so one set of coordinates serves all
        // four. The grass sheets' plain tile is a file of its own; the snow
        // sheet's is on the sheet.
        private static readonly string[] GroundSheets =
        {
            Pack + "Tiles/Grass/Grass_Tiles_2",
            Pack + "Tiles/Grass/Grass_Tiles_1",
            Pack + "Tiles/Grass/Grass_Tiles_3",
            "Cute_Fantasy_Christmas/Decorations/Christmass_Grass",
        };

        private static readonly string[] PlainTiles =
        {
            Pack + "Tiles/Grass/Grass_2_Middle",
            Pack + "Tiles/Grass/Grass_1_Middle",
            Pack + "Tiles/Grass/Grass_3_Middle",
            null,
        };

        // Where each tree's trunk meets the ground: 16 px above the bottom of
        // every frame, whatever its size.
        private static readonly string[] TreeFiles =
        {
            "Small_Oak_Tree", "Medium_Oak_Tree", "Big_Oak_Tree",
            "Small_Spruce_Tree", "Medium_Spruce_Tree", "Big_Spruce_tree",
            "Small_Fruit_Tree", "Medium_Fruit_Tree", "Big_Fruit_Tree",
        };

        // The lowest opaque row of each rock, counted from the top of its 16 px frame.
        private static readonly int[] RockBottoms = { 11, 11, 13, 11, 11, 11, 11, 10, 14, 13 };

        private readonly List<Object> owned;
        private readonly List<Sprite[]> sheets = new List<Sprite[]>();
        // By Tool - 1, then facing, then frame.
        private Sprite[][] tools;

        // Sheets 0 and 1 are the body and the hands, which everyone shares.
        private int[] shoes, pants, hair;
        private int[] forager, woodcutter, stoneGatherer, farmer, builder, unemployed;

        private ArtSet(List<Object> owned) => this.owned = owned;

        public Ground[] Seasons { get; } = new Ground[4];
        public Tile Water { get; private set; }

        // Bare earth for a road (#158), the same in every season: a trodden
        // lane shows through the snow.
        public Tile Path { get; private set; }

        // Path's pixels, and the RoadCorner tiles made so far by their key.
        private Color32[] pathPixels;
        private readonly Dictionary<int, Tile> roadCorners = new Dictionary<int, Tile>();

        // Water that moves, for a few cells among the plain ones: faint
        // sparkles, a droplet's ring now and then, and a fish. Each comes in
        // AccentPhases copies started at different frames, so neighbours do
        // not move in step.
        public const int AccentPhases = 4;

        // Swaying grass and flickering campfires, in frames per second.
        public const float DecorFramesPerSecond = 6f;

        // Each fruit comes in four sizes of bush; spring's fruit is red,
        // summer's purple and autumn's orange, and the fourth is no fruit.
        public const int BushSizes = 4;
        public const int BareBush = 3;

        public static int BushFruitOf(Season season)
        {
            switch (season)
            {
                case Season.Spring:
                case Season.Summer:
                case Season.Autumn:
                    return (int)season;
                case Season.Winter:
                    return BareBush;
                default:
                    // An undefined season would index past the fruit (#141 review).
                    throw new System.ArgumentOutOfRangeException(nameof(season), season, "Not a defined Season.");
            }
        }

        // Berries.png's red berries, darkest first, recoloured for autumn's
        // orange and for a bush with nothing on it, where the berries take the
        // leaves' greens. Only these exact colours change; the leaves and the
        // outline are shared by every fruit.
        private static readonly Color32[] RedBerries =
            { new Color32(0x7A, 0x29, 0x34, 255), new Color32(0xC6, 0x2A, 0x37, 255), new Color32(0xD8, 0x46, 0x50, 255), new Color32(0xEB, 0xA2, 0xA9, 255) };
        private static readonly Color32[] OrangeBerries =
            { new Color32(0x8A, 0x4A, 0x1C, 255), new Color32(0xD9, 0x74, 0x1F, 255), new Color32(0xF0, 0x95, 0x3A, 255), new Color32(0xF8, 0xD2, 0x9A, 255) };
        private static readonly Color32[] Leaves =
            { new Color32(0x1E, 0x6F, 0x50, 255), new Color32(0x33, 0x98, 0x4B, 255), new Color32(0x33, 0x98, 0x4B, 255), new Color32(0x5A, 0xC5, 0x4F, 255) };

        public LoopTile[] Sparkles { get; private set; }
        public LoopTile[] Droplets { get; private set; }
        public LoopTile[] Fish { get; private set; }
        public Sprite[] Trees { get; private set; }
        public Sprite[] Rocks { get; private set; }

        // Flowers and sprouts lie flat on the ground and draw under everyone;
        // berry bushes, on scrub, stand up and sort like trees. Each is one
        // frame, or several for the grass that sways.
        public Sprite[][] FlatDecor { get; private set; }

        // Berry bushes by fruit, then size: (fruit, size) is
        // Bushes[fruit * BushSizes + size]. Fruit is BushFruitOf a season,
        // or BareBush for one picked clean or in winter (#26).
        public Sprite[] Bushes { get; private set; }

        // Each tree's stump, in Trees order: what a felled tree leaves (#26).
        public Sprite[] Stumps { get; private set; }

        // The same scenery as tiles, for the quarter and half size art, where
        // a tilemap stands it up rather than a renderer each (#131): one per
        // tree, rock and bush, and AccentPhases per flower or sprout, each
        // starting its sway a different part of the way through.
        public Tile[] TreeTiles { get; private set; }
        public Tile[] RockTiles { get; private set; }
        public Tile[] BushTiles { get; private set; }
        public Tile[] StumpTiles { get; private set; }
        public TileBase[][] FlatDecorTiles { get; private set; }

        public Sprite BigTent { get; private set; }
        public Sprite SmallTent { get; private set; }
        public Sprite[] Campfire { get; private set; }

        // What only a settled camp has: a well, a woodpile and a stone pile.
        public Sprite Well { get; private set; }
        public Sprite Woodpile { get; private set; }
        public Sprite StonePile { get; private set; }

        // Wall and roof colours of one house design and of the barn, picked
        // per building from its id (#150).
        public Sprite[] Houses { get; private set; }
        public Sprite[] Barns { get; private set; }

        // Storage (#98): the silo for a Granary, and for a Woodshed a fenced
        // log yard on its footprint at each fill level, Woodsheds[level] for
        // level 0 (empty) to YardLevels (full).
        public Sprite[] Granaries { get; private set; }
        public Sprite[] Woodsheds { get; private set; }

        // A yard's three log slots each hold a small stack, then a tall pile.
        public const int YardLevels = 6;

        // Tilled soil, the nine pieces of a bordered patch: FarmLand[row * 3 +
        // column], where row and column are 0 on a field's north or west edge,
        // 2 on its south or east edge, and 1 inside.
        public Tile[] FarmLand { get; private set; }

        // Wheat from sown to ripe, a cell each: seed, sprouts, in flower, ripe.
        public Tile[] Wheat { get; private set; }

        public static int ShoreMask(bool topLeft, bool topRight, bool bottomLeft, bool bottomRight) =>
            (topLeft ? 1 : 0) | (topRight ? 2 : 0) | (bottomLeft ? 4 : 0) | (bottomRight ? 8 : 0);

        // Null without the art submodule, which a public clone never has, and
        // null with an error logged when the submodule is there but a file is
        // missing or badly imported. Everything created is added to `owned`,
        // for the caller to destroy.
        public static ArtSet Load(List<Object> owned)
        {
            if (Resources.Load<Texture2D>(Player + "Player_Base/Player_Base_animations") == null) return null;
            var art = new ArtSet(owned);
            return art.LoadTerrain() && art.LoadScenery() && art.PackScenery() && art.LoadBuildings() && art.LoadVillagers() && art.LoadTools() ? art : null;
        }

        public Ground GroundOf(Season season) => Seasons[(int)season];

        // The sprite for one layer of one person's doll. `look` is from LookOf.
        public Sprite Frame(Look look, int layer, Motion motion, Facing facing, int frame)
        {
            var sheet = sheets[look.Sheets[layer]];
            return sheet[((int)motion * 3 + (int)facing) * FramesPerPose + frame];
        }

        // The sprite for a tool in hand, drawn over the doll and standing
        // on the same spot; null for Tool.None.
        public Sprite ToolFrame(Tool tool, Facing facing, int frame) =>
            tool == Tool.None ? null : tools[(int)tool - 1][(int)facing * FramesPerPose + frame];

        // What someone at work does, by job (#126). A farmer goes by the
        // field: the hoe on bare soil, the watering can while the wheat
        // grows, the hoe again once it is ripe. A builder raising a field is
        // sowing it, so takes the hoe too, and the hammer to anything else.
        // `field` is the stage FieldLook draws: 1 bare, 2-4 growing, 5 ripe;
        // 0 when the work is not on a field.
        public static (Motion Motion, Tool Tool) WorkOf(JobKind job, int field)
        {
            switch (job)
            {
                case JobKind.Woodcutter:
                    return (Motion.Chop, Tool.Axe);
                case JobKind.StoneGatherer:
                    return (Motion.Swing, Tool.Pickaxe);
                case JobKind.Forager:
                    return (Motion.Swing, Tool.Sickle);
                case JobKind.Builder:
                    return (Motion.Swing, field == 0 ? Tool.Hammer : Tool.Hoe);
                case JobKind.Farmer:
                    return field >= 2 && field <= 4 ? (Motion.Water, Tool.WateringCan) : (Motion.Swing, Tool.Hoe);
                default:
                    return (Motion.Idle, Tool.None);
            }
        }

        // Which sheets dress someone: the shirt by job, the rest from `seed`,
        // which the caller derives from the person's id so a person always
        // looks the same. Grey hair is left for elders (#125). Allocates, so
        // call it when someone's job changes rather than every frame.
        public Look LookOf(JobKind job, uint seed)
        {
            // A Forester (#155) dresses as a woodcutter: the art has no shirt
            // of its own for one yet.
            var shirts = job == JobKind.Forager ? forager
                : job == JobKind.Woodcutter || job == JobKind.Forester ? woodcutter
                : job == JobKind.StoneGatherer ? stoneGatherer
                : job == JobKind.Farmer ? farmer
                : job == JobKind.Builder ? builder
                : unemployed;
            return new Look(new[]
            {
                0,
                shoes[(int)(seed % (uint)shoes.Length)],
                pants[(int)(seed / 3u % (uint)pants.Length)],
                shirts[(int)(seed / 17u % (uint)shirts.Length)],
                1,
                hair[(int)(seed / 101u % (uint)hair.Length)],
            });
        }

        private bool LoadTerrain()
        {
            var water = Texture(Pack + "Tiles/Water/Water_Middle");
            var sparkles = Texture(Pack + "Tiles/Water/Water_Middle_Anim_2");
            var droplets = Texture(Pack + "Tiles/Water/Water_Middle_Anim_1");
            var fish = Texture(Pack + "Tiles/Water/Fish_Animated_Tile");
            if (water == null || sparkles == null || droplets == null || fish == null) return false;
            var still = Cut(water, 0, 0, PixelsPerCell, PixelsPerCell, new Vector2(0.5f, 0.5f));
            Water = NewTile(still);
            Sparkles = Phased(Strip(sparkles, 16, 16, new Vector2(0.5f, 0.5f)));
            // A droplet's ring spreads, then the water lies still for three
            // times as long before the next one.
            var ring = new List<Sprite>(Strip(droplets, 16, 16, new Vector2(0.5f, 0.5f)));
            for (var i = ring.Count * 3; i > 0; i--) ring.Add(still);
            Droplets = Phased(ring.ToArray());
            Fish = Phased(Strip(fish, 16, 16, new Vector2(0.5f, 0.5f)));

            for (var season = 0; season < GroundSheets.Length; season++)
            {
                var sheet = Texture(GroundSheets[season]);
                var plain = PlainTiles[season] == null ? null : Texture(PlainTiles[season]);
                if (sheet == null || (PlainTiles[season] != null && plain == null)) return false;
                var pixels = ReadPixels(sheet);
                Seasons[season] = LoadGround(sheet, pixels, plain);
                // The spring sheet's patch of path, columns 0-2 and rows 5-7,
                // has plain path in its middle; the snow sheet has none.
                if (season != 0) continue;
                Path = NewTile(Cut(sheet, PixelsPerCell, 6 * PixelsPerCell, PixelsPerCell, PixelsPerCell, new Vector2(0.5f, 0.5f)));
                pathPixels = PiecePixels(sheet, pixels, 1, 6);
            }
            return true;
        }

        // The tile at a corner with road on `road` of its cells and water on
        // `water`, both by ShoreMask (#158): path with this season's grass
        // edging it, made the first time it is asked for and kept. It is
        // opaque, so it carries its own path, and it stands in for the
        // shoreline piece as well when the corner has water, keeping its bank
        // with the road's grass edge between path and bank. Two roads meeting
        // only at a corner touch at the corner point (Pinched).
        public Tile RoadCorner(Season season, int road, int water)
        {
            var key = ((int)season * 16 + road) * 16 + water;
            if (roadCorners.TryGetValue(key, out var made)) return made;

            var ground = Seasons[(int)season];
            // The grass edging the road, water side too, so a road beside the
            // river reads as path, grass, then bank, as the bank does
            // anywhere. All four cells road: no grass edge.
            var ring = road == ShoreMask(true, false, false, true) ? Pinched(ground, ShoreMask(true, false, false, false), ShoreMask(false, false, false, true))
                : road == ShoreMask(false, true, true, false) ? Pinched(ground, ShoreMask(false, true, false, false), ShoreMask(false, false, true, false))
                : ground.RoadRingPixels[road];
            var shore = ground.ShorePixels[water];
            // The shoreline piece's twin without the bank: the sheets draw
            // the same grass edge on both, so the bank is where they differ,
            // in a colour the twin does not use. The islands redraw a few
            // pixels of their edge in the grass's other greens.
            var unbanked = ground.RoadRingPixels[water];
            var grass = new HashSet<int>();
            if (unbanked != null) foreach (var colour in unbanked) if (colour.a > 0) grass.Add(Packed(colour));
            var pixels = new Color32[PixelsPerCell * PixelsPerCell];
            var half = PixelsPerCell / 2;
            for (var y = 0; y < PixelsPerCell; y++)
            {
                for (var x = 0; x < PixelsPerCell; x++)
                {
                    var i = y * PixelsPerCell + x;
                    // Texture rows count up, so the top two quarters are y >= half.
                    var quarter = y >= half
                        ? (x < half ? ShoreMask(true, false, false, false) : ShoreMask(false, true, false, false))
                        : (x < half ? ShoreMask(false, false, true, false) : ShoreMask(false, false, false, true));
                    // Water on two opposite corners has no shoreline piece,
                    // and DrawnWater never leaves it; were it there, the
                    // water would show through.
                    if ((water & quarter) != 0) pixels[i] = shore != null ? shore[i] : default;
                    // On a road, the grass edging it lies over the bank, as
                    // grass does on any bank.
                    else if ((road & quarter) != 0 && ring != null && ring[i].a > 0) pixels[i] = ring[i];
                    // The bank keeps its own pixels; its grass gives way to
                    // the road's.
                    else if (shore != null && shore[i].a > 0 && !Same(shore[i], unbanked[i]) && !grass.Contains(Packed(shore[i]))) pixels[i] = shore[i];
                    else pixels[i] = ring != null && ring[i].a > 0 ? ring[i] : pathPixels[i];
                }
            }

            var texture = Own(new Texture2D(PixelsPerCell, PixelsPerCell, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Road corner" });
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            made = NewTile(Cut(texture, 0, 0, PixelsPerCell, PixelsPerCell, new Vector2(0.5f, 0.5f)));
            roadCorners.Add(key, made);
            return made;
        }

        // Two roads meeting only at a corner, which has no piece: the edges
        // of the pieces for each alone, pinched together at the corner. Each
        // pixel is path where either piece's hole is, and otherwise the
        // grass of whichever hole is nearer, so each keeps its own edge.
        private static Color32[] Pinched(Ground ground, int first, int second)
        {
            var a = ground.RoadRingPixels[first];
            var b = ground.RoadRingPixels[second];
            var pixels = new Color32[a.Length];
            for (var y = 0; y < PixelsPerCell; y++)
            {
                for (var x = 0; x < PixelsPerCell; x++)
                {
                    var i = y * PixelsPerCell + x;
                    if (a[i].a == 0 || b[i].a == 0) continue;
                    pixels[i] = HoleDistance(first, x, y) <= HoleDistance(second, x, y) ? a[i] : b[i];
                }
            }
            return pixels;
        }

        // How far a pixel is, in pixels, from the quarter a one-corner mask
        // names, in texture order: the top quarters are y >= half.
        private static int HoleDistance(int mask, int x, int y)
        {
            var half = PixelsPerCell / 2;
            var left = mask == ShoreMask(true, false, false, false) || mask == ShoreMask(false, false, true, false);
            var top = mask == ShoreMask(true, false, false, false) || mask == ShoreMask(false, true, false, false);
            var dx = left ? Mathf.Max(0, x - (half - 1)) : Mathf.Max(0, half - x);
            var dy = top ? Mathf.Max(0, half - y) : Mathf.Max(0, y - (half - 1));
            return Mathf.Max(dx, dy);
        }

        // One 16 px piece of a sheet read by ReadPixels, counted from the top
        // left, in texture order: its bottom row first.
        private static Color32[] PiecePixels(Texture2D sheet, Color32[] pixels, int column, int row)
        {
            var piece = new Color32[PixelsPerCell * PixelsPerCell];
            var rect = FromTop(sheet, column * PixelsPerCell, row * PixelsPerCell, PixelsPerCell, PixelsPerCell);
            for (var y = 0; y < PixelsPerCell; y++)
                for (var x = 0; x < PixelsPerCell; x++)
                    piece[y * PixelsPerCell + x] = pixels[((int)rect.y + y) * sheet.width + (int)rect.x + x];
            return piece;
        }

        private static int Packed(Color32 c) => c.r | c.g << 8 | c.b << 16 | c.a << 24;

        private static bool Same(Color32 a, Color32 b) => Packed(a) == Packed(b);

        // One season's ground, from a sheet counted in 16 px tiles from the top
        // left: a ring of ground around a hole (columns 3-5, rows 0-2) and an
        // island of ground in water (columns 3-4, rows 3-4), each transparent
        // where the water shows through from below, and three tufted variants
        // of plain ground (columns 5-7, row 9). The same ring and island
        // without the bank (columns 0-2, rows 0-4) edge a road (#158), with
        // the path showing through. The snow sheet stops after row 4, so
        // winter has no tufts, and its plain ground is at (5, 4). `pixels`
        // is the sheet as ReadPixels reads it, for RoadCorner.
        private Ground LoadGround(Texture2D sheet, Color32[] pixels, Texture2D plain)
        {
            Tile Piece(int column, int row) => NewTile(Cut(sheet, column * PixelsPerCell, row * PixelsPerCell, PixelsPerCell, PixelsPerCell, new Vector2(0.5f, 0.5f)));
            var ground = plain == null
                ? new Ground(Piece(5, 4), new Tile[0])
                : new Ground(NewTile(Cut(plain, 0, 0, PixelsPerCell, PixelsPerCell, new Vector2(0.5f, 0.5f))), new[] { Piece(5, 9), Piece(6, 9), Piece(7, 9) });
            Edges(ground.Shore, ground.ShorePixels, 3);
            Edges(ground.RoadEdges, ground.RoadRingPixels, 0);
            return ground;

            // A ring whose left column is `left`, rows 0-2, and the island
            // under it, rows 3-4, by which corners show through.
            void Edges(Tile[] pieces, Color32[][] piecePixels, int left)
            {
                void Put(int mask, int column, int row)
                {
                    pieces[mask] = Piece(column, row);
                    piecePixels[mask] = PiecePixels(sheet, pixels, column, row);
                }

                Put(ShoreMask(false, false, false, true), left, 0);
                Put(ShoreMask(false, false, true, true), left + 1, 0);
                Put(ShoreMask(false, false, true, false), left + 2, 0);
                Put(ShoreMask(false, true, false, true), left, 1);
                Put(ShoreMask(true, false, true, false), left + 2, 1);
                Put(ShoreMask(false, true, false, false), left, 2);
                Put(ShoreMask(true, true, false, false), left + 1, 2);
                Put(ShoreMask(true, false, false, false), left + 2, 2);
                Put(ShoreMask(true, true, true, false), left, 3);
                Put(ShoreMask(true, true, false, true), left + 1, 3);
                Put(ShoreMask(true, false, true, true), left, 4);
                Put(ShoreMask(false, true, true, true), left + 1, 4);
                // Two opposite corners only have no piece. For water the
                // caller draws a land cell there as water, so the two never
                // meet; RoadCorner pinches two roads' pieces together.
            }
        }

        private bool LoadScenery()
        {
            var trees = new List<Sprite>();
            var stumps = new List<Sprite>();
            foreach (var name in TreeFiles)
            {
                // Three frames side by side: a stump, the tree, and the tree
                // without its shadow.
                var texture = Texture(Pack + "Trees/" + name);
                if (texture == null) return false;
                var width = texture.width / 3;
                stumps.Add(Cut(texture, 0, 0, width, texture.height, new Vector2(0.5f, 16f / texture.height)));
                trees.Add(Cut(texture, width, 0, width, texture.height, new Vector2(0.5f, 16f / texture.height)));
            }
            Trees = trees.ToArray();
            Stumps = stumps.ToArray();

            var rocks = new Sprite[RockBottoms.Length];
            for (var i = 0; i < rocks.Length; i++)
            {
                // The first frame of a rock's break animation is the unbroken rock.
                var texture = Texture(Decor + "Outdoor_Decor_Animations/Rock_Animations/Rock_" + (i + 1) + "_Anim");
                if (texture == null) return false;
                rocks[i] = Cut(texture, 0, 0, 16, 16, new Vector2(0.5f, (15f - RockBottoms[i]) / 16f));
            }
            Rocks = rocks;

            // Outdoor_Decor.png in 16 px tiles from the top left: flowers in
            // rows 0-1 and the right half of row 2, sprouts in the left half.
            var decor = Texture(Decor + "Outdoor_Decor");
            if (decor == null) return false;
            Sprite Item(int column, int row, float pivotY) => Cut(decor, column * 16, row * 16, 16, 16, new Vector2(0.5f, pivotY));
            var flat = new List<Sprite[]>();
            for (var column = 0; column < 6; column++)
            {
                flat.Add(new[] { Item(column, 0, 0.5f) });
                flat.Add(new[] { Item(column, 1, 0.5f) });
                flat.Add(new[] { Item(column, 2, 0.5f) });
            }
            for (var i = 1; i <= 3; i++)
            {
                var sway = Texture(Decor + "Outdoor_Decor_Animations/Grass_Animations/Grass_" + i + "_Anim");
                if (sway == null) return false;
                flat.Add(Strip(sway, 16, 16, new Vector2(0.5f, 0.5f)));
            }
            for (var i = 1; i <= 6; i++)
            {
                var sway = Texture(Decor + "Outdoor_Decor_Animations/Grass_Animations/Flower_Grass_" + i + "_Anim");
                if (sway == null) return false;
                flat.Add(Strip(sway, 16, 16, new Vector2(0.5f, 0.5f)));
            }
            FlatDecor = flat.ToArray();

            // Berry bushes for scrub (#137): Berries.png in 16 px tiles, red
            // berries down column 0 and purple down column 2, four sizes each.
            // Orange and bare are the red column recoloured (#26).
            var berries = Texture(Pack + "Crops/Berries");
            if (berries == null) return false;
            var pixels = ReadPixels(berries);
            var orange = Recoloured(berries, pixels, OrangeBerries, "Orange berries");
            var bare = Recoloured(berries, pixels, Leaves, "Bare bushes");
            var bushes = new List<Sprite>();
            void Column(Texture2D sheet, int left)
            {
                for (var row = 0; row < BushSizes; row++) bushes.Add(Cut(sheet, left, row * 16, 16, 16, new Vector2(0.5f, 2f / 16f)));
            }
            Column(berries, 0);
            Column(berries, 32);
            Column(orange, 0);
            Column(bare, 0);
            Bushes = bushes.ToArray();

            // Tents stand on the ground 15 px above the bottom of their frame;
            // the campfire's logs sit on the bottom of each 16x32 frame.
            var bigTent = Texture(Pack + "Buildings/Buildings/Tent/Tent_Big");
            var smallTent = Texture(Pack + "Buildings/Buildings/Tent/Tent_Small");
            var fire = Texture(Decor + "Outdoor_Decor_Animations/Other_Animations/Campfire_Anim");
            var well = Texture(Decor + "Well");
            var ores = Texture(Decor + "Ores");
            if (bigTent == null || smallTent == null || fire == null || well == null || ores == null) return false;
            // The well stands 2 px above the bottom of its frame; the woodpile
            // is the stack at column 1, rows 12-13 of Outdoor_Decor, standing 5
            // px above its bottom; the stone pile is the grey heap second in
            // Ores.png's first row, 1 px above its bottom.
            Well = Cut(well, 0, 0, well.width, well.height, new Vector2(0.5f, 2f / well.height));
            Woodpile = Cut(decor, 16, 192, 16, 32, new Vector2(0.5f, 5f / 32f));
            StonePile = Cut(ores, 16, 0, 16, 16, new Vector2(0.5f, 1f / 16f));
            BigTent = Cut(bigTent, 0, 0, bigTent.width, bigTent.height, new Vector2(0.5f, 15f / bigTent.height));
            SmallTent = Cut(smallTent, 0, 0, smallTent.width, smallTent.height, new Vector2(0.5f, 15f / smallTent.height));
            Campfire = Strip(fire, 16, 32, new Vector2(0.5f, 0f));
            return true;
        }

        // Every tree, rock, bush, flower and sprout copied into one texture on
        // the GPU, and the scenery re-cut from it (#131). A tilemap in chunk
        // mode batches its tiles by texture, and keeps them in row order -
        // a tree in front of the one north of it - only among tiles that
        // share one. Each piece keeps a transparent border, so a zoomed-out
        // edge never picks up its neighbour.
        private bool PackScenery()
        {
            const int width = 512;
            const int border = 1;
            var pieces = new List<Sprite>();
            pieces.AddRange(Trees);
            pieces.AddRange(Stumps);
            pieces.AddRange(Rocks);
            pieces.AddRange(Bushes);
            foreach (var frames in FlatDecor) pieces.AddRange(frames);

            // Shelves left to right, tallest first, so each shelf wastes little.
            var order = new List<int>();
            for (var i = 0; i < pieces.Count; i++) order.Add(i);
            order.Sort((a, b) => pieces[b].textureRect.height.CompareTo(pieces[a].textureRect.height));
            var at = new Vector2Int[pieces.Count];
            int x = 0, y = 0, shelf = 0;
            foreach (var i in order)
            {
                var rect = pieces[i].textureRect;
                var w = (int)rect.width + 2 * border;
                var h = (int)rect.height + 2 * border;
                if (x + w > width)
                {
                    x = 0;
                    y += shelf;
                    shelf = 0;
                }
                at[i] = new Vector2Int(x + border, y + border);
                x += w;
                shelf = Mathf.Max(shelf, h);
            }

            var height = y + shelf;
            var atlas = Own(new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Scenery atlas" });
            // Cleared, so the borders are transparent; then only on the GPU.
            atlas.SetPixels32(new Color32[width * height]);
            atlas.Apply(false, true);

            var packed = new Sprite[pieces.Count];
            for (var i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (piece.texture.format != TextureFormat.RGBA32)
                {
                    // As for the villagers: the GPU copy needs RGBA32.
                    Debug.LogError("ArtSet: " + piece.texture.name + " is " + piece.texture.format + ", not RGBA32; reimport Assets/Art.");
                    return false;
                }
                var rect = piece.textureRect;
                Graphics.CopyTexture(piece.texture, 0, 0, (int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height, atlas, 0, 0, at[i].x, at[i].y);
                var pivot = new Vector2(piece.pivot.x / rect.width, piece.pivot.y / rect.height);
                packed[i] = Own(Sprite.Create(atlas, new Rect(at[i].x, at[i].y, rect.width, rect.height), pivot, PixelsPerCell, 0, SpriteMeshType.FullRect));
            }

            var next = 0;
            Sprite[] Take(int count)
            {
                var taken = new Sprite[count];
                System.Array.Copy(packed, next, taken, 0, count);
                next += count;
                return taken;
            }
            Trees = Take(Trees.Length);
            Stumps = Take(Stumps.Length);
            Rocks = Take(Rocks.Length);
            Bushes = Take(Bushes.Length);
            for (var i = 0; i < FlatDecor.Length; i++) FlatDecor[i] = Take(FlatDecor[i].Length);

            TreeTiles = System.Array.ConvertAll(Trees, NewTile);
            StumpTiles = System.Array.ConvertAll(Stumps, NewTile);
            RockTiles = System.Array.ConvertAll(Rocks, NewTile);
            BushTiles = System.Array.ConvertAll(Bushes, NewTile);
            FlatDecorTiles = new TileBase[FlatDecor.Length][];
            for (var i = 0; i < FlatDecor.Length; i++)
            {
                if (FlatDecor[i].Length == 1)
                {
                    FlatDecorTiles[i] = new TileBase[] { NewTile(FlatDecor[i][0]) };
                    continue;
                }
                var phased = Phased(FlatDecor[i]);
                foreach (var tile in phased) tile.framesPerSecond = DecorFramesPerSecond;
                FlatDecorTiles[i] = phased;
            }
            return true;
        }

        private bool LoadBuildings()
        {
            // A house's walls meet the ground 16 px above the bottom of its
            // frame, a barn's 15 px, the silo's 5 px.
            var houses = new List<Sprite>();
            var barns = new List<Sprite>();
            foreach (var walls in new[] { "Base", "Green", "Red" })
            {
                foreach (var roof in new[] { "Black", "Blue", "Red" })
                {
                    var house = Texture(Pack + "Buildings/Buildings/Houses/Wood/House_1_Wood_" + walls + "_" + roof);
                    var barn = Texture(Pack + "Buildings/Buildings/Unique_Buildings/Barn/Barn_" + walls + "_" + roof);
                    if (house == null || barn == null) return false;
                    houses.Add(Cut(house, 0, 0, house.width, house.height, new Vector2(0.5f, 16f / house.height)));
                    barns.Add(Cut(barn, 0, 0, barn.width, barn.height, new Vector2(0.5f, 15f / barn.height)));
                }
            }
            Houses = houses.ToArray();
            Barns = barns.ToArray();

            var silo = Texture(Pack + "Buildings/Buildings/Unique_Buildings/Silo/Silo");
            if (silo == null || !LoadYards()) return false;
            Granaries = new[] { Cut(silo, 0, 0, silo.width, silo.height, new Vector2(0.5f, 5f / silo.height)) };

            // FarmLand_Tile.png in 16 px tiles from the top left: the bordered
            // patch is columns 1-3, rows 0-2. Crops.png has a crop to every
            // two rows, wheat first: its seed, sprouts, flowering and ripe
            // wheat are columns 2-5 of row 1.
            var soil = Texture(Pack + "Tiles/FarmLand/FarmLand_Tile");
            var crops = Texture(Pack + "Crops/Crops");
            if (soil == null || crops == null) return false;
            FarmLand = new Tile[9];
            for (var row = 0; row < 3; row++)
                for (var column = 0; column < 3; column++)
                    FarmLand[row * 3 + column] = NewTile(Cut(soil, (column + 1) * PixelsPerCell, row * PixelsPerCell, PixelsPerCell, PixelsPerCell, new Vector2(0.5f, 0.5f)));
            Wheat = new Tile[4];
            for (var stage = 0; stage < Wheat.Length; stage++)
                Wheat[stage] = NewTile(Cut(crops, (stage + 2) * PixelsPerCell, PixelsPerCell, PixelsPerCell, PixelsPerCell, new Vector2(0.5f, 0.5f)));
            return true;
        }

        // The Woodshed (#98) is a fenced log yard drawn for its 5x4 footprint:
        // Fences.png's 16 px pieces round the edge, a cell to each, with a gate
        // in the middle of the front; and three log slots along the middle,
        // each Outdoor_Decor's small stack and then its tall pile. Composed
        // once for every fill level, side by side in one texture.
        private const int YardWidth = 5, YardHeight = 4;
        private const int YardBase = 46; // where the logs stand, from the yard's top
        private static readonly int[] YardSlots = { 11, 30, 49 }; // the tall piles' left edges
        private static readonly RectInt SmallStack = new RectInt(1, 197, 14, 24);
        private static readonly RectInt TallPile = new RectInt(7, 259, 20, 42);

        // Each cell of the fence, from the top left: (cell, piece) pairs, the
        // piece as its column and row on Fences.png. Back row and sides first,
        // so the logs stand in front of them, then the front row over the logs.
        private static readonly (Vector2Int cell, Vector2Int piece)[] BackFence =
        {
            (new Vector2Int(0, 0), new Vector2Int(1, 1)), (new Vector2Int(1, 0), new Vector2Int(2, 0)),
            (new Vector2Int(2, 0), new Vector2Int(2, 0)), (new Vector2Int(3, 0), new Vector2Int(2, 0)),
            (new Vector2Int(4, 0), new Vector2Int(3, 1)),
            (new Vector2Int(0, 1), new Vector2Int(0, 1)), (new Vector2Int(4, 1), new Vector2Int(0, 1)),
            (new Vector2Int(0, 2), new Vector2Int(0, 1)), (new Vector2Int(4, 2), new Vector2Int(0, 1)),
        };
        private static readonly (Vector2Int cell, Vector2Int piece)[] FrontFence =
        {
            (new Vector2Int(0, 3), new Vector2Int(1, 3)), (new Vector2Int(1, 3), new Vector2Int(3, 0)),
            (new Vector2Int(3, 3), new Vector2Int(1, 0)), (new Vector2Int(4, 3), new Vector2Int(3, 3)),
        };

        private bool LoadYards()
        {
            var spec = BuildingTable.Of(BuildingKind.Woodshed);
            if (spec.Width != YardWidth || spec.Height != YardHeight)
            {
                Debug.LogError("ArtSet: the Woodshed's yard is drawn for " + YardWidth + "x" + YardHeight + " cells, not " + spec.Width + "x" + spec.Height + ".");
                return false;
            }
            var fences = Texture(Decor + "Fences");
            var decor = Texture(Decor + "Outdoor_Decor");
            if (fences == null || decor == null) return false;
            var fence = new Sheet(ReadPixels(fences), fences.width, fences.height);
            var logs = new Sheet(ReadPixels(decor), decor.width, decor.height);

            const int width = YardWidth * PixelsPerCell, height = YardHeight * PixelsPerCell;
            var yards = new Sheet(new Color32[width * (YardLevels + 1) * height], width * (YardLevels + 1), height);
            for (var level = 0; level <= YardLevels; level++)
            {
                var left = level * width;
                foreach (var (cell, piece) in BackFence) Fence(yards, left, cell, fence, piece);
                for (var slot = 0; slot < YardSlots.Length; slot++)
                {
                    // A slot fills once with a small stack, and again with a tall pile.
                    var x = left + YardSlots[slot];
                    if (level > YardSlots.Length + slot) Paste(yards, x, YardBase - TallPile.height, logs, TallPile);
                    else if (level > slot) Paste(yards, x + (TallPile.width - SmallStack.width) / 2, YardBase - SmallStack.height, logs, SmallStack);
                }
                foreach (var (cell, piece) in FrontFence) Fence(yards, left, cell, fence, piece);
            }

            var texture = Own(new Texture2D(yards.Width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Woodshed yards" });
            texture.SetPixels32(yards.Pixels);
            texture.Apply(false, true);
            Woodsheds = new Sprite[YardLevels + 1];
            for (var level = 0; level <= YardLevels; level++)
                Woodsheds[level] = Own(Sprite.Create(texture, new Rect(level * width, 0, width, height), new Vector2(0.5f, 0f), PixelsPerCell, 0, SpriteMeshType.FullRect));
            return true;
        }

        private static void Fence(Sheet to, int left, Vector2Int cell, Sheet fence, Vector2Int piece) =>
            Paste(to, left + cell.x * PixelsPerCell, cell.y * PixelsPerCell, fence,
                new RectInt(piece.x * PixelsPerCell, piece.y * PixelsPerCell, PixelsPerCell, PixelsPerCell));

        // Pixels read back from a texture, bottom row first as Unity keeps them.
        private readonly struct Sheet
        {
            public readonly Color32[] Pixels;
            public readonly int Width, Height;

            public Sheet(Color32[] pixels, int width, int height)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
            }

            public int IndexFromTop(int x, int y) => (Height - 1 - y) * Width + x;
        }

        // Lays `piece` of `sheet` (both counted from the top left) over `to`
        // at (x, y) from its top left, the way a sprite drawn over another
        // would: what is behind shows through the part-transparent shadows.
        private static void Paste(Sheet to, int x, int y, Sheet sheet, RectInt piece)
        {
            for (var dy = 0; dy < piece.height; dy++)
            {
                for (var dx = 0; dx < piece.width; dx++)
                {
                    var top = sheet.Pixels[sheet.IndexFromTop(piece.x + dx, piece.y + dy)];
                    if (top.a == 0) continue;
                    var i = to.IndexFromTop(x + dx, y + dy);
                    var under = to.Pixels[i];
                    var a = top.a / 255f;
                    var b = under.a / 255f * (1f - a);
                    var alpha = a + b;
                    to.Pixels[i] = new Color32(
                        (byte)Mathf.RoundToInt((top.r * a + under.r * b) / alpha),
                        (byte)Mathf.RoundToInt((top.g * a + under.g * b) / alpha),
                        (byte)Mathf.RoundToInt((top.b * a + under.b * b) / alpha),
                        (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
        }

        private bool LoadVillagers()
        {
            var paths = new List<string> { Player + "Player_Base/Player_Base_animations", Player + "Hands/Hands_1_Bare" };
            int[] Add(IEnumerable<string> more)
            {
                var added = new List<int>();
                foreach (var path in more)
                {
                    added.Add(paths.Count);
                    paths.Add(path);
                }
                return added.ToArray();
            }

            shoes = Add(Each(ShoeColours, c => Player + "Feet/Shoes_1_" + c));
            pants = Add(Each(PantsColours, c => Player + "Legs/OG_Pants/Pants_1_" + c));
            forager = Add(Each(new[] { "Blue", "Green", "Orange", "White_and_Brown" }, c => Player + "Chest/Farmer_Shirt/Farmer_Shirt_1_" + c));
            woodcutter = Add(Each(new[] { "Red", "Green", "Blue", "Brown" }, c => Player + "Chest/Lumberjack_Shirt/Lumberjack_Shirt_1_" + c));
            // Farmers and builders wear the shirts of the trades nearest theirs,
            // in the colours foragers and woodcutters do not, as stone gatherers
            // and the jobless share the plain shirt (#150).
            farmer = Add(Each(new[] { "Black", "Pink", "Purple", "Red" }, c => Player + "Chest/Farmer_Shirt/Farmer_Shirt_1_" + c));
            builder = Add(Each(new[] { "Black", "Orange", "Pink", "Purple", "White" }, c => Player + "Chest/Lumberjack_Shirt/Lumberjack_Shirt_1_" + c));
            stoneGatherer = Add(Each(new[] { "Black", "Brown", "Purple", "Blue" }, c => Player + "Chest/OG_Shirt/Shirt_1_" + c));
            unemployed = Add(Each(new[] { "Green", "Red", "Orange", "Pink" }, c => Player + "Chest/OG_Shirt/Shirt_1_" + c));
            var hairs = new List<string>();
            for (var style = 1; style <= 6; style++)
                foreach (var colour in HairColours) hairs.Add(Player + "Head/Hair_" + style + "/Hair_" + style + "_" + colour);
            hair = Add(hairs);

            return BuildAtlas(paths);
        }

        // Every villager frame in one texture, so a crowd is one texture and
        // not forty: the size #18's stress test has to match. Each sheet holds
        // 64 px frames (the art repository keeps only the poses drawn), so
        // each frame's middle is copied into the atlas on the GPU and the
        // sheet unloaded.
        private bool BuildAtlas(List<string> paths)
        {
            // A sheet's block is a pose to a row: taller than it is wide, so
            // more go across than down to keep the atlas roughly square.
            const int blockWidth = FramesPerPose * Crop, blockHeight = Poses * Crop;
            var across = Mathf.CeilToInt(Mathf.Sqrt(paths.Count * (float)blockHeight / blockWidth));
            var down = Mathf.CeilToInt(paths.Count / (float)across);
            var atlas = Own(new Texture2D(across * blockWidth, down * blockHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Villager atlas" });

            for (var i = 0; i < paths.Count; i++)
            {
                var source = CopyableTexture(paths[i]);
                if (source == null) return false;
                if (source.height < Poses * SheetFrame)
                {
                    Debug.LogError("ArtSet: " + paths[i] + " has " + source.height / SheetFrame + " rows, not " + Poses + "; rerun Update-Resources.ps1 in Assets/Art.");
                    return false;
                }

                var blockX = i % across * blockWidth;
                var blockY = (down - 1 - i / across) * blockHeight;
                var sprites = new Sprite[Poses * FramesPerPose];
                for (var pose = 0; pose < Poses; pose++)
                {
                    for (var frame = 0; frame < FramesPerPose; frame++)
                    {
                        var from = FromTop(source, frame * SheetFrame + CropLeft, pose * SheetFrame + CropTop, Crop, Crop);
                        var toX = blockX + frame * Crop;
                        var toY = blockY + (Poses - 1 - pose) * Crop;
                        Graphics.CopyTexture(source, 0, 0, (int)from.x, (int)from.y, Crop, Crop, atlas, 0, 0, toX, toY);
                        sprites[pose * FramesPerPose + frame] = Own(Sprite.Create(atlas, new Rect(toX, toY, Crop, Crop), FeetPivot, PixelsPerCell, 0, SpriteMeshType.FullRect));
                    }
                }
                sheets.Add(sprites);
                Resources.UnloadAsset(source);
            }
            return true;
        }

        // Every tool's frames in one small texture of their own, cut wider
        // than the doll's (ToolLeft and the rest), as BuildAtlas cuts the doll.
        private bool LoadTools()
        {
            var rows = ToolSheets.Length * 3;
            var atlas = Own(new Texture2D(FramesPerPose * ToolWidth, rows * ToolHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Tool atlas" });
            tools = new Sprite[ToolSheets.Length][];
            var loaded = new Dictionary<string, Texture2D>();
            for (var t = 0; t < ToolSheets.Length; t++)
            {
                var (path, first) = ToolSheets[t];
                if (!loaded.TryGetValue(path, out var source))
                {
                    source = CopyableTexture(path);
                    if (source == null) return false;
                    loaded.Add(path, source);
                }
                tools[t] = new Sprite[3 * FramesPerPose];
                for (var facing = 0; facing < 3; facing++)
                {
                    var row = t * 3 + facing;
                    for (var frame = 0; frame < FramesPerPose; frame++)
                    {
                        var from = FromTop(source, frame * SheetFrame + ToolLeft, (first + facing) * SheetFrame + ToolTop, ToolWidth, ToolHeight);
                        var toX = frame * ToolWidth;
                        var toY = (rows - 1 - row) * ToolHeight;
                        Graphics.CopyTexture(source, 0, 0, (int)from.x, (int)from.y, ToolWidth, ToolHeight, atlas, 0, 0, toX, toY);
                        tools[t][facing * FramesPerPose + frame] = Own(Sprite.Create(atlas, new Rect(toX, toY, ToolWidth, ToolHeight), ToolPivot, PixelsPerCell, 0, SpriteMeshType.FullRect));
                    }
                }
            }
            foreach (var source in loaded.Values) Resources.UnloadAsset(source);
            return true;
        }

        // A sheet the GPU copies above can read: PixelArtImport keeps these
        // uncompressed RGBA32, and anything else is a stale import.
        private static Texture2D CopyableTexture(string path)
        {
            var source = Texture(path);
            if (source == null || source.format == TextureFormat.RGBA32) return source;
            Debug.LogError("ArtSet: " + path + " is " + source.format + ", not RGBA32; reimport Assets/Art.");
            return null;
        }

        // A copy of `source` with each colour in RedBerries swapped for the
        // matching one in `to`, alpha kept: the berries alone change colour.
        // The purple column's colours match none of them, and is left as is.
        // RGBA32 and on the GPU only, as PackScenery's copy needs.
        private Texture2D Recoloured(Texture2D source, Color32[] pixels, Color32[] to, string name)
        {
            var copy = (Color32[])pixels.Clone();
            for (var i = 0; i < copy.Length; i++)
            {
                for (var c = 0; c < RedBerries.Length; c++)
                {
                    // Within a step or two: the read goes through the GPU and
                    // back, which need not return every channel exactly.
                    var from = RedBerries[c];
                    if (Mathf.Abs(copy[i].r - from.r) > 2 || Mathf.Abs(copy[i].g - from.g) > 2 || Mathf.Abs(copy[i].b - from.b) > 2) continue;
                    copy[i] = new Color32(to[c].r, to[c].g, to[c].b, copy[i].a);
                    break;
                }
            }
            var texture = Own(new Texture2D(source.width, source.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name });
            texture.SetPixels32(copy);
            texture.Apply(false, true);
            return texture;
        }

        // The art is imported unreadable, which keeps it off the CPU for the
        // game's lifetime; a copy through the GPU reads it once.
        public static Color32[] ReadPixels(Texture2D texture)
        {
            // The active target is saved before the blit, which sets it:
            // saved after, it would "restore" the temporary released below.
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            var pixels = copy.GetPixels32();
            Object.Destroy(copy);
            return pixels;
        }

        private static Texture2D Texture(string path)
        {
            var texture = Resources.Load<Texture2D>(path);
            if (texture == null) Debug.LogError("ArtSet: Assets/Art/Resources/" + path + " is missing; the art submodule is incomplete.");
            return texture;
        }

        // AccentPhases looping tiles of one animation, each starting a
        // different part of the way through it.
        private LoopTile[] Phased(Sprite[] frames)
        {
            var tiles = new LoopTile[AccentPhases];
            for (var phase = 0; phase < tiles.Length; phase++)
            {
                var shift = phase * frames.Length / AccentPhases;
                var shifted = new Sprite[frames.Length];
                for (var i = 0; i < frames.Length; i++) shifted[i] = frames[(i + shift) % frames.Length];
                tiles[phase] = Own(ScriptableObject.CreateInstance<LoopTile>());
                tiles[phase].frames = shifted;
            }
            return tiles;
        }

        // An animation laid out as frames side by side along one row.
        private Sprite[] Strip(Texture2D texture, int width, int height, Vector2 pivot)
        {
            var frames = new Sprite[texture.width / width];
            for (var i = 0; i < frames.Length; i++) frames[i] = Cut(texture, i * width, 0, width, height, pivot);
            return frames;
        }

        private Sprite Cut(Texture2D texture, int left, int top, int width, int height, Vector2 pivot) =>
            Own(Sprite.Create(texture, FromTop(texture, left, top, width, height), pivot, PixelsPerCell, 0, SpriteMeshType.FullRect));

        private Tile NewTile(Sprite sprite)
        {
            var tile = Own(ScriptableObject.CreateInstance<Tile>());
            tile.sprite = sprite;
            return tile;
        }

        // Art tools and the packs count from the top left; textures count up
        // from the bottom.
        private static Rect FromTop(Texture2D texture, int left, int top, int width, int height) =>
            new Rect(left, texture.height - top - height, width, height);

        private static IEnumerable<string> Each(string[] colours, System.Func<string, string> path)
        {
            foreach (var colour in colours) yield return path(colour);
        }

        private T Own<T>(T asset) where T : Object
        {
            owned.Add(asset);
            return asset;
        }

        // A doll's rows, three to a motion (Facing order): idle and walk,
        // then the swings people work with (#126), which the pack draws
        // tool-less for a Tool to go over. Swing is one overhead stroke for
        // every long-handled tool; the pack's separate pickaxe swing differs
        // from it by a few pixels, so it is not kept.
        public enum Motion
        {
            Idle,
            Walk,
            Water,
            Chop,
            Swing,
        }

        // What someone holds while working; None while idle or walking.
        public enum Tool
        {
            None,
            Axe,
            Pickaxe,
            Hoe,
            WateringCan,
            Hammer,
            Sickle,
        }

        public enum Facing
        {
            Down,
            // Faces right; flip the sprite to face left.
            Side,
            Up,
        }

        // One sheet index per layer.
        public readonly struct Look
        {
            public Look(int[] sheets) => Sheets = sheets;

            public int[] Sheets { get; }
        }

        // One season's land tiles.
        public sealed class Ground
        {
            public Ground(Tile plain, Tile[] tufts)
            {
                Plain = plain;
                Tufts = tufts;
            }

            public Tile Plain { get; }

            // Plain ground with tufts of grass; none under snow.
            public Tile[] Tufts { get; }

            // Shoreline pieces by which corners of a tile are water (see
            // ShoreMask); null where nothing is drawn.
            public Tile[] Shore { get; } = new Tile[16];

            // The grass edging a road (#158), by which corners of a tile are
            // road, as Shore is by water: for measuring, since what is drawn
            // is ArtSet.RoadCorner, made from these pieces' pixels.
            public Tile[] RoadEdges { get; } = new Tile[16];

            // RoadEdges' and Shore's pixels in texture order, null where
            // there is no piece.
            public Color32[][] RoadRingPixels { get; } = new Color32[16][];
            public Color32[][] ShorePixels { get; } = new Color32[16][];
        }
    }

    // What each piece of art looks like from far away (#130): its average
    // colour, and how much of a cell it covers. The zoomed-out map colours a
    // cell by laying these over each other the way the art view lays the
    // pieces - ground, then shoreline, then whatever stands on it - which is
    // the art view's cell shrunk to one pixel without drawing anything.
    //
    // Measured once, when the art is loaded, rather than saved: it takes a few
    // milliseconds, and a saved table would go stale the day a sprite changed.
    public sealed class ArtColours
    {
        // Premultiplied colour and coverage, both as a share of one cell (or,
        // for a shoreline quadrant, of the quarter cell it lies over), coverage
        // at most one: a tree bigger than its cell covers its cell, no more.
        private readonly Dictionary<Texture2D, Color32[]> read = new Dictionary<Texture2D, Color32[]>();

        private ArtColours()
        {
        }

        public Vector4[] Plain { get; } = new Vector4[4];
        public Vector4[][] Tufts { get; } = new Vector4[4][];

        // By season, then ShoreMask, then quadrant: 0 bottom-left, 1
        // bottom-right, 2 top-left, 3 top-right, in texture space.
        public Vector4[][][] Shore { get; } = new Vector4[4][][];
        // The same for the grass edging a road (#158).
        public Vector4[][][] RoadEdges { get; } = new Vector4[4][][];
        public Vector4 Path { get; private set; }
        public Vector4 Water { get; private set; }
        public Vector4[] Trees { get; private set; }
        public Vector4[] Rocks { get; private set; }
        public Vector4[] Bushes { get; private set; }
        public Vector4[] FlatDecor { get; private set; }

        public static ArtColours Measure(ArtSet art)
        {
            var colours = new ArtColours();
            for (var season = 0; season < 4; season++)
            {
                var ground = art.Seasons[season];
                colours.Plain[season] = colours.Of(ground.Plain.sprite);
                colours.Tufts[season] = new Vector4[ground.Tufts.Length];
                for (var i = 0; i < ground.Tufts.Length; i++) colours.Tufts[season][i] = colours.Of(ground.Tufts[i].sprite);
                colours.Shore[season] = colours.Quadrants(ground.Shore);
                colours.RoadEdges[season] = colours.Quadrants(ground.RoadEdges);
            }
            colours.Path = colours.Of(art.Path.sprite);
            colours.Water = colours.Of(art.Water.sprite);
            colours.Trees = colours.Each(art.Trees);
            colours.Rocks = colours.Each(art.Rocks);
            colours.Bushes = colours.Each(art.Bushes);
            colours.FlatDecor = new Vector4[art.FlatDecor.Length];
            for (var i = 0; i < art.FlatDecor.Length; i++) colours.FlatDecor[i] = colours.Of(art.FlatDecor[i][0]);
            colours.read.Clear();
            return colours;
        }

        // `layer` over `under`, both premultiplied, with the layer covering
        // `share` of the cell.
        public static Vector4 Over(Vector4 under, Vector4 layer, float share = 1f) =>
            under * (1f - layer.w * share) + layer * share;

        public static Color32 ToColour(Vector4 premultiplied)
        {
            var a = Mathf.Max(premultiplied.w, 1e-4f);
            return new Color32(
                (byte)Mathf.Clamp(premultiplied.x / a * 255f, 0f, 255f),
                (byte)Mathf.Clamp(premultiplied.y / a * 255f, 0f, 255f),
                (byte)Mathf.Clamp(premultiplied.z / a * 255f, 0f, 255f),
                255);
        }

        // Each corner piece's four quadrants, by mask; zero where a mask has
        // no piece.
        private Vector4[][] Quadrants(Tile[] pieces)
        {
            var result = new Vector4[pieces.Length][];
            for (var mask = 0; mask < pieces.Length; mask++)
            {
                var quadrants = new Vector4[4];
                var piece = pieces[mask];
                if (piece != null)
                {
                    var half = ArtSet.PixelsPerCell / 2;
                    for (var q = 0; q < 4; q++) quadrants[q] = Of(piece.sprite, new RectInt(q % 2 * half, q / 2 * half, half, half));
                }
                result[mask] = quadrants;
            }
            return result;
        }

        private Vector4[] Each(Sprite[] sprites)
        {
            var result = new Vector4[sprites.Length];
            for (var i = 0; i < sprites.Length; i++) result[i] = Of(sprites[i]);
            return result;
        }

        private Vector4 Of(Sprite sprite)
        {
            var rect = sprite.textureRect;
            return Of(sprite, new RectInt(0, 0, (int)rect.width, (int)rect.height), ArtSet.PixelsPerCell * ArtSet.PixelsPerCell);
        }

        private Vector4 Of(Sprite sprite, RectInt area) => Of(sprite, area, area.width * area.height);

        // The premultiplied sum over `area` (in the sprite's own pixels,
        // counted up from its bottom left) as a share of `cellPixels`.
        private Vector4 Of(Sprite sprite, RectInt area, int cellPixels)
        {
            var texture = sprite.texture;
            var pixels = Pixels(texture);
            var rect = sprite.textureRect;
            var sum = Vector4.zero;
            for (var y = area.yMin; y < area.yMax; y++)
            {
                for (var x = area.xMin; x < area.xMax; x++)
                {
                    var c = pixels[((int)rect.y + y) * texture.width + (int)rect.x + x];
                    var a = c.a / 255f;
                    sum += new Vector4(c.r / 255f * a, c.g / 255f * a, c.b / 255f * a, a);
                }
            }
            var share = sum / cellPixels;
            return share.w > 1f ? share / share.w : share;
        }

        // Each texture read once (ArtSet.ReadPixels), however many pieces
        // are cut from it.
        private Color32[] Pixels(Texture2D texture)
        {
            if (read.TryGetValue(texture, out var pixels)) return pixels;
            pixels = ArtSet.ReadPixels(texture);
            read.Add(texture, pixels);
            return pixels;
        }
    }
}
