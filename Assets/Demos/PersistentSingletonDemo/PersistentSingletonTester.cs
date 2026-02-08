using Laubrary.Cookbook;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Cookbook.Samples
{
    public class PersistentSingletonTester : MonoBehaviour
    {
        public PersistMyData persistentData;

        void Start()
        {
            persistentData = PersistMyData.I;
        }

        public void Save()
        {
            persistentData.SaveData();
        }

        public void Load()
        {
            persistentData.LoadData();
        }

        [Serializable]
        public class PersistMyData : PersistentSingleton<PersistMyData>
        {
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
}