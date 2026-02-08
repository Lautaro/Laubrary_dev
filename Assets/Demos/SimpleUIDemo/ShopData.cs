using UnityEngine;
using System;

namespace Laubrary.SimpleUI.Demo
{
    [SimpleUI(autoRefresh: true)]
    public class ShopData
    {
        public string shopName = "The Rusty Sword";
        public int itemsInStock = 42;
        public float goldMultiplier = 1.0f;
        
        public string currentTime => DateTime.Now.ToString("HH:mm:ss");
    }
}
