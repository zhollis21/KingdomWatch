using UnityEngine;
using UnityEngine.Tilemaps;

namespace KingdomWatch.Game
{
    // A tile that loops through frames, such as rippling water (#121). The
    // Tilemap animates it by itself; nothing has to update it each frame.
    public sealed class LoopTile : TileBase
    {
        public Sprite[] frames;
        public float framesPerSecond = 4f;

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            tileData.sprite = frames[0];
            tileData.colliderType = Tile.ColliderType.None;
        }

        public override bool GetTileAnimationData(Vector3Int position, ITilemap tilemap, ref TileAnimationData tileAnimationData)
        {
            tileAnimationData.animatedSprites = frames;
            tileAnimationData.animationSpeed = framesPerSecond;
            return true;
        }
    }
}
