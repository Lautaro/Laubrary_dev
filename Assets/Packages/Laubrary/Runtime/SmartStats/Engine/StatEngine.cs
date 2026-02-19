//using Assets;
//using Lautaro.Stats.Engine;
//using System;
//using System.Collections;
//using System.Collections.Generic;
//using Unity.VisualScripting;
//using UnityEngine;

//public class StatEngine  : MonoBehaviour 
//{
//    public static StatEngine instance;

//    public static void Register(StatBase stat, GameObject parent)
//    {
//        if (instance == null) CreateInstance();
//        instance.registeredStats.Add(stat, parent);
//    }
//    public static bool IsRegistered(StatBase stat)
//    {
//        return false;// instance.registeredStats.Values.Contains(stat);
//    }

//    static void CreateInstance()
//    {
//        if (instance != null)
//            return;

//        GameObject newObject = new GameObject("StatEngine");
//        instance = newObject.AddComponent<StatEngine>();
//        newObject.transform.position = new Vector3(0, 0, 0);
//    }

//   Dictionary<StatBase, GameObject> registeredStats = new Dictionary<StatBase, GameObject>();

//    private void Awake()
//    {
//        if (instance != null)
//            DestroyImmediate(this);
//    }

//    // Update is called once per frame
//    void Update()
//    {
//        if (Input.GetKeyDown(KeyCode.Alpha1))
//        {
//            Debug.Log("Count Dracula: " + registeredStats.Count);
//            foreach (var item in registeredStats)
//            {
//                Debug.Log(item.Value.name + " -> " + item.Key.ToString());
//            }
//        }

//        PurgeOrphans();

//        UpdateStats();
//    }

//    private void UpdateStats()
//    {
//        foreach (var kvp in registeredStats)
//        {
//            if (kvp.Key != null)
//                kvp.Key.Update();
//        }
//    }

//    private void PurgeOrphans()
//    {
//        var flaggedForRemoval = new List<StatBase>();
//        foreach (var kvp in registeredStats)
//        {
//            if (kvp.Value == null)
//            {
//                flaggedForRemoval.Add(kvp.Key);
//            }
//        }

//        foreach (var stat in flaggedForRemoval)
//        {
//            registeredStats.Remove(stat);
//        }
//    }

//}
