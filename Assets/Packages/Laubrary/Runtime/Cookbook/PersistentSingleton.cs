using UnityEngine;
using System.IO;

namespace Laubrary.Cookbook
{
    /// <summary>
    /// The data in deriving class can be saved and loaded. 
    /// Uses Unity JsonUtilities.
    /// Saves data in a "Application.persistentDataPath" folder. So the data will not be exported when built and installed on another device. 
    /// The following types can be saved inside a class inheriting from PersistentSingleton:
    /// 1. Primitive Types: int, float, double, bool, char, byte, sbyte, short, ushort, uint, long, ulong, decimal
    /// 2. Unity Types: Vector2, Vector3, Vector4, Quaternion, Color, Rect, Matrix4x4
    /// 3. Strings
    /// 4. Arrays and Lists: Arrays and List<T> where T is a supported type (including other serializable classes)
    /// 5. Serializable Classes: Custom classes marked with the [System.Serializable] attribute; Classes with public fields or properties that are of supported types
    /// </summary>
    public abstract class PersistentSingleton<T> where T : PersistentSingleton<T>, new()
    {
        private static T _instance;

        public static T I
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new T();
                    //_instance.LoadData();
                }
                return _instance;
            }
        }
        public string GetFilePath => Path.Combine(Application.persistentDataPath, "singleton_repository/" + typeof(T).Name + ".json");
        
        public void SaveData()
        {
            string directoryPath = Path.GetDirectoryName(GetFilePath);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string jsonData = JsonUtility.ToJson(this, true);
            File.WriteAllText(GetFilePath, jsonData);
        }

        public void LoadData()
        {
            if (File.Exists(GetFilePath))
            {
                string jsonData = File.ReadAllText(GetFilePath);
                JsonUtility.FromJsonOverwrite(jsonData, this);
            }
        }
    }
}