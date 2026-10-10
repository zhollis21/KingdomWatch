using System;
using System.Globalization;
using System.Text;
using KingdomWatch.Core;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Settlements;
using KingdomWatch.Core.Traversal;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// Draws a settlement's layout from Core's state as an SVG: terrain,
    /// roads, the camp yard, and every building by kind with its door and
    /// roof rows (#23). What the town planner is looked at by off the
    /// Unity Editor, so it reads the world and changes nothing.
    /// </summary>
    internal static class TownPicture
    {
        // Pixels a cell.
        private const int Cell = 12;

        // Room under the map for the legend.
        private const int LegendHeight = 64;

        public static string Draw(World world, Settlement settlement, int radius, string title)
        {
            if (world is null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (settlement is null)
            {
                throw new ArgumentNullException(nameof(settlement));
            }

            var grid = world.Grid;
            var centre = settlement.Position;
            var minX = Math.Max(0, centre.X - radius);
            var minY = Math.Max(0, centre.Y - radius);
            var maxX = Math.Min(grid.Width - 1, centre.X + radius);
            var maxY = Math.Min(grid.Height - 1, centre.Y + radius);
            var width = (maxX - minX + 1) * Cell;
            var height = (maxY - minY + 1) * Cell;

            var svg = new StringBuilder();
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(width).Append("\" height=\"")
                .Append(height + LegendHeight + 28).Append("\" font-family=\"sans-serif\">\n");
            svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fbfaf5\"/>\n");
            svg.Append("<text x=\"6\" y=\"19\" font-size=\"14\" font-weight=\"bold\" fill=\"#222\">").Append(Escape(title)).Append("</text>\n");
            svg.Append("<g transform=\"translate(0,28)\">\n");

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var at = new WorldPosition(x, y);
                    Rect(svg, x - minX, y - minY, 1, 1, Ground(grid[at]), null);

                    if (grid.RoadAt(at) != RoadGrade.None)
                    {
                        Rect(svg, x - minX, y - minY, 1, 1, "#c8a46a", null);
                    }
                }
            }

            // The yard, outlined.
            var yard = Buildings.CampYardRadius;
            Rect(svg, centre.X - yard - minX, centre.Y - yard - minY, (2 * yard) + 1, (2 * yard) + 1, "none", "#7a5a2a\" stroke-dasharray=\"4 3");
            Rect(svg, centre.X - minX, centre.Y - minY, 1, 1, "#e0582a", null);

            var buildings = world.Buildings.All;

            for (var i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];

                if (building.Settlement != settlement.Id)
                {
                    continue;
                }

                var spec = BuildingTable.Of(building.Kind);
                var left = building.Anchor.X - minX;
                var top = building.Anchor.Y - minY;

                if (spec.Clearance > 0)
                {
                    Rect(svg, left, top - spec.Clearance, spec.Width, spec.Clearance, "#00000014", null);
                }

                var fill = Fill(building);
                Rect(svg, left, top, spec.Width, spec.Height, fill, building.IsComplete ? "#222" : "#222\" stroke-dasharray=\"3 2");

                if (Buildings.HasDoor(building.Kind))
                {
                    var door = Buildings.DoorOf(building.Kind, building.Anchor);
                    svg.Append("<circle cx=\"").Append(((door.X - minX) * Cell) + (Cell / 2)).Append("\" cy=\"")
                        .Append(((door.Y - minY) * Cell) + (Cell / 2)).Append("\" r=\"3\" fill=\"#222\"/>\n");
                }
            }

            svg.Append("</g>\n");
            Legend(svg, height + 28 + 10);
            svg.Append("</svg>\n");
            return svg.ToString();
        }

        private static void Legend(StringBuilder svg, int top)
        {
            var items = new (string Fill, string Label)[]
            {
                ("#a8cf7c", "plains"), ("#4f7d3f", "forest"), ("#86a35a", "scrub"), ("#a19c86", "rocks"),
                ("#5d93c4", "water"), ("#c8a46a", "road"), ("#b5583a", "house"), ("#7b2d26", "barn"),
                ("#e8c84a", "field"), ("#e0582a", "camp"),
            };

            for (var i = 0; i < items.Length; i++)
            {
                var x = 6 + ((i % 5) * 104);
                var y = top + ((i / 5) * 22);
                svg.Append("<rect x=\"").Append(x).Append("\" y=\"").Append(y).Append("\" width=\"14\" height=\"14\" fill=\"")
                    .Append(items[i].Fill).Append("\" stroke=\"#222\" stroke-width=\"0.5\"/>\n");
                svg.Append("<text x=\"").Append(x + 20).Append("\" y=\"").Append(y + 12).Append("\" font-size=\"12\" fill=\"#222\">")
                    .Append(items[i].Label).Append("</text>\n");
            }
        }

        private static string Ground(TerrainKind kind)
        {
            switch (kind)
            {
                case TerrainKind.Plains: return "#a8cf7c";
                case TerrainKind.Forest: return "#4f7d3f";
                case TerrainKind.Scrub: return "#86a35a";
                case TerrainKind.Rocks: return "#a19c86";
                case TerrainKind.SmallRiver: return "#5d93c4";
                case TerrainKind.DeepWater: return "#3c6c9c";
                default: return "#ff00ff";
            }
        }

        private static string Fill(Building building)
        {
            switch (building.Kind)
            {
                case BuildingKind.House: return "#b5583a";
                case BuildingKind.Barn: return "#7b2d26";
                case BuildingKind.Field: return "#e8c84a";
                default: return "#ff00ff";
            }
        }

        private static void Rect(StringBuilder svg, int x, int y, int w, int h, string fill, string? stroke)
        {
            svg.Append("<rect x=\"").Append(x * Cell).Append("\" y=\"").Append(y * Cell).Append("\" width=\"")
                .Append(w * Cell).Append("\" height=\"").Append(h * Cell).Append("\" fill=\"").Append(fill).Append('"');

            if (stroke != null)
            {
                svg.Append(" stroke=\"").Append(stroke).Append("\" stroke-width=\"1.2\"");
            }

            svg.Append("/>\n");
        }

        private static string Escape(string text) =>
            text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);

        internal static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
