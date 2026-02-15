using System;
using System.Collections.Generic;
using UnityEngine.Events;

namespace Laubrary.Notifyer
{
    public abstract class NotifyerEventBase { }

    public class NotifyerEventBase<T> : NotifyerEventBase
    {
        public T Data { get; set; }

        public NotifyerEventBase(T data)
        {
            Data = data;
        }
        public NotifyerEventBase()
        {
        }
        public override string ToString()
        {
            return Data?.ToString() ?? base.ToString();
        }
    }

    public class Notifyer
    {
        private static readonly Notifyer instance = new Notifyer();
        private readonly Dictionary<Type, List<Delegate>> notificationTypes = new Dictionary<Type, List<Delegate>>();
        private readonly Dictionary<string, List<Delegate>> textNotifications = new Dictionary<string, List<Delegate>>();
        private static readonly Dictionary<string, object> references = new Dictionary<string, object>();

        public static event Action<NotifyerEventBase, string> OnAnyEvent;
        public static event Action<string, string> OnAnyStringEvent;

        /// <summary>Subscribes to a notification event with a specific data type.</summary>
        public static void Subscribe<T>(UnityAction<T> callback) where T : NotifyerEventBase
        {
            var type = typeof(T);
            if (!instance.notificationTypes.ContainsKey(type))
            {
                instance.notificationTypes[type] = new List<Delegate>();
            }
            instance.notificationTypes[type].Add(callback);
        }

        /// <summary>Unsubscribes from a notification event with a specific data type.</summary>
        public static void Unsubscribe<T>(UnityAction<T> callback) where T : NotifyerEventBase
        {
            var type = typeof(T);
            if (instance.notificationTypes.ContainsKey(type))
            {
                var delegates = instance.notificationTypes[type];
                delegates.Remove(callback);

                if (delegates.Count == 0)
                {
                    instance.notificationTypes.Remove(type);
                }
            }
        }

        /// <summary>Notifies all subscribers of an event instance with a specific data type.</summary>
        public static void Notify<T>(T eventInstance, string logMessage ="") where T : NotifyerEventBase
        {
            var type = eventInstance.GetType(); // Get the actual type of the event instance
            if (instance.notificationTypes.TryGetValue(type, out var list))
            {
                foreach (Delegate del in list)
                {
                    (del as UnityAction<T>)?.Invoke(eventInstance);
                }
            }
            OnAnyEvent?.Invoke(eventInstance, logMessage);
        }

        /// <summary>Subscribes to a notification event with a string identifier.</summary>
        public static void Subscribe(string notificationId, UnityAction callback)
        {
            if (!instance.textNotifications.ContainsKey(notificationId))
            {
                instance.textNotifications[notificationId] = new List<Delegate>();
            }
            instance.textNotifications[notificationId].Add(callback);
        }

        /// <summary>Notifies all subscribers of an event with a string identifier. Throws an exception if no subscribers are found and throwError is true.</summary>
        public static void Notify(string notificationId, string logMessage = "", bool throwError = false)
        {
            if (instance.textNotifications.TryGetValue(notificationId, out var list))
            {
                foreach (Delegate del in list)
                {
                    (del as UnityAction)?.Invoke();
                }
            }
            else
            {
                if (throwError)
                    throw new Exception("Notifyer is trying to notify with notiticiation message id:" + notificationId + " but no one is receiving.");
            }
            OnAnyStringEvent?.Invoke(notificationId, logMessage);
        }

        /// <summary>Adds a reference object for easy access with a specified key.</summary>
        public static void AddReference<T>(T obj, string key) where T : class
        {
            references[key] = obj;
        }

        /// <summary>Retrieves a reference object by its key with the right type casted.</summary>
        public static T Reference<T>(string key) where T : class
        {
            if (references.TryGetValue(key, out object value))
            {
                return value as T;
            }

            return null;
        }
    }
}