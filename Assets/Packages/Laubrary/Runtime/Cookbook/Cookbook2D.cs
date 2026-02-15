using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace Laubrary.Cookbook2D
{
    public static class Cookbook2D
    {
        public static Vector2 Add(this Vector2 v2, float addX, float addY)
        {
            v2.x += addX;
            v2.y += addY;
            return v2;
        }

        public static Vector2 SetX(this Vector2 v2, float setX)
        {
            v2.x = setX;
            return v2;
        }

        public static Vector2 SetY(this Vector2 v2, float setY)
        {
            v2.y = setY;
            return v2;
        }

        /// <summary>
        /// Places two gameobject next to each other by their SpriteRenderer bounds. Set where they should touch by specifying BoundsPositions. Set Space parameter to add space between the 2 points. Space uses the absolute value. 
        /// </summary>
        public static void PlaceNextTo(this SpriteRenderer renderer1, NineSlicePosition position1, SpriteRenderer renderer2, NineSlicePosition position2, float space = 0)
        {
            space = Mathf.Abs(space);
            // Get world positions for the specified enum positions
            Vector2 worldPosition1 = renderer1.GetWorldPosition2D(position1);
            Vector2 worldPosition2 = renderer2.GetWorldPosition2D(position2);


            // Calculate the directional vector from renderer2 to renderer1
            Vector2 targetRendererCenter = renderer2.GetWorldPosition2D(NineSlicePosition.Center);
            Vector2 direction = (worldPosition1 - targetRendererCenter).normalized;

            // Calculate the offset needed to align the specified position of renderer1 with the worldPosition2
            Vector2 offset1 = worldPosition1 - (Vector2)renderer1.transform.position;

            // Apply the space offset in the direction of the vector
            Vector2 spaceOffset = direction * space;

            // Set the new position for renderer1's GameObject considering the offset for the specified position and space
            renderer1.transform.position = (worldPosition2 + spaceOffset) - offset1;
        }


        /// <summary>
        /// Gets the world space position of a NineSlicePosition on the sprite renderer. 
        /// </summary>
        public static Vector2 GetWorldPosition2D(this SpriteRenderer renderer, NineSlicePosition position)
        {
            Bounds bounds = renderer.bounds;
            Vector2 worldPosition = renderer.transform.position; // Start with the GameObject's world position

            switch (position)
            {
                case NineSlicePosition.TopCenter:
                    worldPosition += new Vector2(0, bounds.extents.y);
                    break;
                case NineSlicePosition.BottomCenter:
                    worldPosition += new Vector2(0, -bounds.extents.y);
                    break;
                case NineSlicePosition.LeftCenter:
                    worldPosition += new Vector2(-bounds.extents.x, 0);
                    break;
                case NineSlicePosition.RightCenter:
                    worldPosition += new Vector2(bounds.extents.x, 0);
                    break;
                case NineSlicePosition.Center:
                    // No adjustment needed
                    break;
                case NineSlicePosition.TopLeft:
                    worldPosition += new Vector2(-bounds.extents.x, bounds.extents.y);
                    break;
                case NineSlicePosition.TopRight:
                    worldPosition += new Vector2(bounds.extents.x, bounds.extents.y);
                    break;
                case NineSlicePosition.BottomLeft:
                    worldPosition += new Vector2(-bounds.extents.x, -bounds.extents.y);
                    break;
                case NineSlicePosition.BottomRight:
                    worldPosition += new Vector2(bounds.extents.x, -bounds.extents.y);
                    break;
            }

            return worldPosition;
        }

        /// <summary>Returns the NineSlicePosition on the screen that contains the transform</summary>
        public static NineSlicePosition GetScreenNineSlicePosition(Vector3 position)
        {
            Vector3 screenPos = Camera.main.WorldToViewportPoint(position);

            // Divide the screen into 3x3 grid, 0.33 and 0.66 being the division points
            if (screenPos.x < 0.33f)
            {
                if (screenPos.y < 0.33f) return NineSlicePosition.BottomLeft;
                else if (screenPos.y > 0.66f) return NineSlicePosition.TopLeft;
                else return NineSlicePosition.LeftCenter;
            }
            else if (screenPos.x > 0.66f)
            {
                if (screenPos.y < 0.33f) return NineSlicePosition.BottomRight;
                else if (screenPos.y > 0.66f) return NineSlicePosition.TopRight;
                else return NineSlicePosition.RightCenter;
            }
            else
            {
                if (screenPos.y < 0.33f) return NineSlicePosition.BottomCenter;
                else if (screenPos.y > 0.66f) return NineSlicePosition.TopCenter;
                else return NineSlicePosition.Center;
            }
        }

        /// <summary>Returns the world space bounds of a nineSlice position in the screen using the main camera</summary>
        /// <param name="boundsPosition"></param>
        /// <returns></returns>
        public static Bounds GetScreenNineSlicePositionBounds(NineSlicePosition boundsPosition, float zDepth=0)
        {
            Camera cam = Camera.main;

            // Determine the world space positions of the corners of the screen at the specified depth
            Vector3 farTopLeft = cam.ViewportToWorldPoint(new Vector3(0, 1, zDepth));
            Vector3 farTopRight = cam.ViewportToWorldPoint(new Vector3(1, 1, zDepth));
            Vector3 farBottomLeft = cam.ViewportToWorldPoint(new Vector3(0, 0, zDepth));
            Vector3 farBottomRight = cam.ViewportToWorldPoint(new Vector3(1, 0, zDepth));

            // Determine the center and size of the Bounds for each slice
            Vector3 center = Vector3.zero;
            Vector3 size = Vector3.zero;

            switch (boundsPosition)
            {
                case NineSlicePosition.TopLeft:
                    center = (farTopLeft + cam.ViewportToWorldPoint(new Vector3(0.33f, 0.66f, zDepth))) / 2;
                    size = new Vector3((farTopRight.x - farTopLeft.x) / 3, (farTopLeft.y - farBottomLeft.y) / 3, 0);
                    break;
                case NineSlicePosition.TopCenter:
                    center = (cam.ViewportToWorldPoint(new Vector3(0.33f, 1, zDepth)) + cam.ViewportToWorldPoint(new Vector3(0.66f, 0.66f, zDepth))) / 2;
                    size = new Vector3((farTopRight.x - farTopLeft.x) / 3, (farTopLeft.y - farBottomLeft.y) / 3, 0);
                    break;
                case NineSlicePosition.TopRight:
                    center = (farTopRight + cam.ViewportToWorldPoint(new Vector3(0.66f, 0.66f, zDepth))) / 2;
                    size = new Vector3((farTopRight.x - farTopLeft.x) / 3, (farTopRight.y - farBottomRight.y) / 3, 0);
                    break;
                case NineSlicePosition.LeftCenter:
                    center = (cam.ViewportToWorldPoint(new Vector3(0, 0.66f, zDepth)) + cam.ViewportToWorldPoint(new Vector3(0.33f, 0.33f, zDepth))) / 2;
                    size = new Vector3((farTopRight.x - farTopLeft.x) / 3, (farTopLeft.y - farBottomLeft.y) / 3, 0);
                    break;
                case NineSlicePosition.Center:
                    center = (cam.ViewportToWorldPoint(new Vector3(0.33f, 0.66f, zDepth)) + cam.ViewportToWorldPoint(new Vector3(0.66f, 0.33f, zDepth))) / 2;
                    size = new Vector3((farTopRight.x - farTopLeft.x) / 3, (farTopLeft.y - farBottomLeft.y) / 3, 0);
                    break;
                case NineSlicePosition.RightCenter:
                    center = (cam.ViewportToWorldPoint(new Vector3(0.66f, 0.66f, zDepth)) + cam.ViewportToWorldPoint(new Vector3(1, 0.33f, zDepth))) / 2;
                    size = new Vector3((farTopRight.x - farTopLeft.x) / 3, (farTopRight.y - farBottomRight.y) / 3, 0);
                    break;
                case NineSlicePosition.BottomLeft:
                    center = (farBottomLeft + cam.ViewportToWorldPoint(new Vector3(0.33f, 0.33f, zDepth))) / 2;
                    size = new Vector3((farBottomRight.x - farBottomLeft.x) / 3, (farTopLeft.y - farBottomLeft.y) / 3, 0);
                    break;
                case NineSlicePosition.BottomCenter:
                    center = (cam.ViewportToWorldPoint(new Vector3(0.33f, 0, zDepth)) + cam.ViewportToWorldPoint(new Vector3(0.66f, 0.33f, zDepth))) / 2;
                    size = new Vector3((farBottomRight.x - farBottomLeft.x) / 3, (farTopLeft.y - farBottomLeft.y) / 3, 0);
                    break;
                case NineSlicePosition.BottomRight:
                    center = (farBottomRight + cam.ViewportToWorldPoint(new Vector3(0.66f, 0.33f, zDepth))) / 2;
                    size = new Vector3((farBottomRight.x - farBottomLeft.x) / 3, (farTopRight.y - farBottomRight.y) / 3, 0);
                    break;
            }

            // Set the z component of the center to the specified depth
            center.z = zDepth;

            // Adjust size to ensure it's positive
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), 0);

            // Create and return the Bounds
            return new Bounds(center, size);
        }


        /// <summary>Gets the position of the NineSlice point of the Bounds object</summary>
        /// <param name="bounds"></param>
        /// <param name="slicePosition"></param>
        /// <returns></returns>
        public static Vector3 GetPoint(this Bounds bounds, NineSlicePosition slicePosition)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3 center = bounds.center;

            switch (slicePosition)
            {
                case NineSlicePosition.TopLeft:
                    return new Vector3(min.x, max.y, center.z);
                case NineSlicePosition.TopCenter:
                    return new Vector3(center.x, max.y, center.z);
                case NineSlicePosition.TopRight:
                    return new Vector3(max.x, max.y, center.z);
                case NineSlicePosition.LeftCenter:
                    return new Vector3(min.x, center.y, center.z);
                case NineSlicePosition.Center:
                    return center;
                case NineSlicePosition.RightCenter:
                    return new Vector3(max.x, center.y, center.z);
                case NineSlicePosition.BottomLeft:
                    return new Vector3(min.x, min.y, center.z);
                case NineSlicePosition.BottomCenter:
                    return new Vector3(center.x, min.y, center.z);
                case NineSlicePosition.BottomRight:
                    return new Vector3(max.x, min.y, center.z);
                default:
                    return center; // Default to center if slice is undefined
            }
        }

        /// <summary>Calculates the direction vector from one transform to another.</summary>
        public static Vector3 DirectionTo(this Transform source, Transform target)
        {
            return (target.position - source.position).normalized;
        }

        /// <summary>Calculates the direction vector from a transform to a V3.</summary>
        public static Vector3 DirectionTo(this Transform source, Vector3 targetPosition)
        {
            return (targetPosition - source.position).normalized;
        }

        /// <summary>Calculates the direction from a V3 to a transform.</summary>
        public static Vector3 DirectionTo(this Vector3 sourcePosition, Transform target)
        {
            return (target.position - sourcePosition).normalized;
        }

        /// <summary>Calculates the direction from a V3 to a V3.</summary>
        public static Vector3 DirectionTo(this Vector3 sourcePosition, Vector3 targetPosition)
        {
            return (targetPosition - sourcePosition).normalized;
        }

        public static void DebugDrawBounds2D(Bounds bounds, Color color, float duration)
        {
            // 2D rectangle vertices on the XZ plane
            var p1 = new Vector2(bounds.min.x, bounds.min.y);
            var p2 = new Vector2(bounds.min.x, bounds.max.y);
            var p3 = new Vector2(bounds.max.x, bounds.max.y);
            var p4 = new Vector2(bounds.max.x, bounds.min.y);

            // Draw 2D rectangle
            Debug.DrawLine(p1, p2, color, duration);
            Debug.DrawLine(p2, p3, color, duration);
            Debug.DrawLine(p3, p4, color, duration);
            Debug.DrawLine(p4, p1, color, duration);
        }

        /// <summary>
        /// Returns a random point inside the bounds of a PolygonCollider2D. 
        /// If a point inside the polygon is not found within the specified number of tries, Vector2.zero is returned.
        /// </summary>
        /// <param name="polyCollider">The PolygonCollider2D within which to find a random point.</param>
        /// <param name="maxTries">The maximum number of attempts to find a random point inside the polygon. 
        /// If set to 0 or less, the method will try indefinitely until a point is found.</param>
        /// <returns>A random point within the polygon if found within the specified attempts, otherwise Vector2.zero.</returns>
        public static Vector2 GetRandomPoint(this PolygonCollider2D polyCollider, int maxTries = 0)
        {
            Bounds bounds = polyCollider.bounds;
            int tries = 1;

            while (maxTries <= 0 || tries < maxTries)
            {
                float x = Random.Range(bounds.min.x, bounds.max.x);
                float y = Random.Range(bounds.min.y, bounds.max.y);
                Vector2 randomPoint = new Vector2(x, y);

                if (polyCollider.OverlapPoint(randomPoint))
                {
                    return randomPoint; // Return as soon as a valid point is found
                }

                tries++;
            }

            // Handle the case where no point was found
            Debug.LogWarning("Failed to find a random point inside the polygon after " + (maxTries > 0 ? maxTries.ToString() : "unlimited") + " tries.");
            return Vector2.zero; // Return a default value or consider another fallback
        }
    }
    public enum NineSlicePosition
    {
        TopLeft, TopCenter, TopRight, LeftCenter, Center, RightCenter, BottomLeft, BottomCenter, BottomRight
    }
}