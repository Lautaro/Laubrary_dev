using Laubrary.Cookbook;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PersistentSingletonTester : MonoBehaviour
{
    public PersistMyData persistentData;
       
    // Start is called before the first frame update
    void Start()
    {
        persistentData = PersistMyData.I;
    }

    [Button]
    public void Save()
    {
        persistentData.SaveData();
    }
    [Button]
    public void Load()
    {
        persistentData.LoadData();
    }

    [Serializable]
    public class PersistMyData : PersistentSingleton<PersistMyData> {
        public string FunText;
        public int FunNumber;
        public List<FunThingsToDo> FunThingsToDo;
    }

    [Serializable]
    public class FunThingsToDo
    {
        public string Activity;
        public List<string> FunPeople;
    }
}


