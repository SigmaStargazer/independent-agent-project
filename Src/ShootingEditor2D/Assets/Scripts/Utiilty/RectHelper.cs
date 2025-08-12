using UnityEngine;

namespace ShootingEditor2D
{
    public class RectHelper
    {
        public static Rect RectForAnchorCenter(Vector2 centerPos, Vector2 size)
        {
            var width = size.x;
            var height = size.y;
            var x = centerPos.x - width * 0.5f;
            var y = centerPos.y - height * 0.5f;
            return new Rect(x, y, width, height);
        }
        public static Rect RectForAnchorCenter(float x, float y, float width, float height)
        {
            var finalX = x - width * 0.5f;
            var finalY = y - height * 0.5f;
            return new Rect(finalX, finalY, width, height);
        }
    }

}
