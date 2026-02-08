using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Laubrary.Randomizers
{
    public static class Randomizers
    {
        /// <summary>
        /// Selects a random item from a collection, excluding items in the provided exclude list. The exclude list is modified to include the selected item.
        /// If all items in the collection are in the exclude list, the exclude list is cleared and a random item is selected from the entire collection.
        /// </summary>
        /// <typeparam name="T">The type of items in the collection.</typeparam>
        /// <param name="collection">The collection from which to select an item.</param>
        /// <param name="exclude">A reference to a list of items to exclude from selection. This list will be modified.</param>
        /// <returns>A randomly selected item from the collection that is not in the exclude list.</returns>

        public static T GetRoundRobinItem<T>(IEnumerable<T> collection, ref List<T> exclude)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            exclude ??= new List<T>();

            List<T> list = collection.Except(exclude).ToList();

            if (list.Count == 0)
            {
                exclude.Clear(); // Clear the exclude list if all items are excluded
                list = collection.ToList(); // Reset the list to include all items
            }

            var random = new System.Random();
            int index = random.Next(0, list.Count);
            var selectedItem = list[index];

            exclude.Add(selectedItem); // Update the exclude list
            return selectedItem;
        }


        /// <summary>Selects a random item from a collection.</summary>
        public static T GetRandomItem<T>(this IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            List<T> list = collection.ToList();

            var random = new System.Random();
            int index = random.Next(0, list.Count);
            return list[index];
        }


        /// <param name="vignette">Value 0-1f. Removes space for the possible random position starting from the border.  </param>
        /// <returns></returns>
        public static Vector2 GetRandomPositionInViewport(this Camera camera, float vignette = 0f)
        {
            // Scale vignette to be within 0 to 0.5f range based on input percentage
            float scaledVignette = vignette * 0.5f;

            float min = scaledVignette;
            float max = 1f - scaledVignette;

            float randomX = Random.Range(min, max);
            float randomY = Random.Range(min, max);

            var randomPos = camera.ViewportToWorldPoint(new Vector3(randomX, randomY, 0));
            randomPos.z = 0;
            return randomPos;
        }

        /// <summary>Camera.Main to get a random position in viewport </summary>
        public static Vector2 GetRandomPositionInViewport(float vignette = 0f)
        {
            return Camera.main.GetRandomPositionInViewport(vignette);
        }

        // Extension method for Bounds to get a random position within, considering vignette
        // Extension method for Bounds to get a random position within, considering vignette
        public static Vector3 GetRandomPositionWithin(this Bounds bounds, float vignette = 0f)
        {
            // Clamp vignette between 0 and 1
            float clampedVignette = Mathf.Clamp(vignette, 0f, 1f);

            // Calculate min and max taking vignette into account
            Vector3 min = bounds.min + clampedVignette * bounds.size * 0.5f; // Adjust min based on vignette
            Vector3 max = bounds.max - clampedVignette * bounds.size * 0.5f; // Adjust max based on vignette

            // Generate a random position within the adjusted bounds
            float x = Random.Range(min.x, max.x);
            float y = Random.Range(min.y, max.y);
            float z = Random.Range(min.z, max.z);

            return new Vector3(x, y, z);
        }
    }

    /// <summary>
    /// Provides round-robin random selection from a collection, ensuring each item is selected once per cycle before any repeats.
    /// Maintains internal state so callers do not need to manage an exclude list.
    /// </summary>
    public class RoundRobinCollection<T>
    {
        private readonly IEnumerable<T> _collection;
        private List<T> _exclude = new List<T>();
        private readonly System.Random _random = new System.Random();

        public RoundRobinCollection(IEnumerable<T> collection)
        {
            _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        }

        public T GetNext()
        {
            var available = _collection.Except(_exclude).ToList();

            if (available.Count == 0)
            {
                _exclude.Clear();
                available = _collection.ToList();
            }

            int index = _random.Next(0, available.Count);
            var selected = available[index];
            _exclude.Add(selected);
            return selected;
        }
    }
}