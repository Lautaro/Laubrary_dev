using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Laubrary.LaubraryTicker;

namespace Laubrary.Quellection
{
    /// <summary>
    /// A light weight tiny unity job system. Each frame only one of the registered methods are called. 
    /// When the last one is called it starts from zero. 
    /// Pros: easy way to make sure heavier methods are not called many times in each frame. 
    /// Cons: the more methods added to a Quellection the longer time between each method is called.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Qullection<T>
    {
        private List<T> items = new List<T>();
        private List<Action> itemMethods = new List<Action>();
        private int currentIndex = 0;
        private string methodName;
        private float lastExecutionTime = -1f;

        /// <summary>
        /// Make sure to call the Destroy method when the hosting GameObject is destroyed.
        /// </summary>
        public Qullection(string methodName)
        {
            this.methodName = methodName;
            Ticker.OnUpdate += ExecuteNext;
        }

        public void Destroy()
        {
            items.Clear();
            itemMethods.Clear();
            Ticker.OnUpdate -= ExecuteNext;
        }

        public void Add(T item)
        {
            if (item != null && !items.Contains(item))
            {
                items.Add(item);
                MethodInfo method = item.GetType().GetMethod(methodName);
                if (method != null)
                {
                    Action action = (Action)Delegate.CreateDelegate(typeof(Action), item, method);
                    itemMethods.Add(action);
                }
                else
                {
                    Debug.LogWarning($"[QUELLECTION] Method {methodName} not found on {item.GetType().Name}");
                    itemMethods.Add(null);
                }
            }
        }

        void ExecuteNext()
        {
            if (lastExecutionTime == Time.time)
                return;

            if (items.Count == 0)
                return;

            if (currentIndex >= items.Count)
                currentIndex = 0;

            T currentItem = items[currentIndex];
            Action currentAction = itemMethods[currentIndex];

            // Prune null references or destroyed game objects
            if (currentItem == null || currentAction == null)
            {
                items.RemoveAt(currentIndex);
                itemMethods.RemoveAt(currentIndex);
                return;
            }

            // Check if currentItem is a MonoBehaviour and if its GameObject is null
            MonoBehaviour monoBehaviour = currentItem as MonoBehaviour;
            if (monoBehaviour != null && monoBehaviour.gameObject == null)
            {
                items.RemoveAt(currentIndex);
                itemMethods.RemoveAt(currentIndex);
                return;
            }

            currentAction.Invoke();

            currentIndex++;
        }
    }
}
