using Laubrary.Pocket;
using UnityEngine;

namespace Laubrary.Quellection.Samples
{
    public class PocketTester : MonoBehaviour
    {
        public Pocket<string> pocketOne = new Pocket<string>();
        public Pocket<string> pocketTwo = new Pocket<string>();
        public int Amount;

        void Start()
        {

        }

        public void RemoveFromPocketOne()
        {
            var returnAmount = pocketOne.Remove(Amount);
            Debug.Log(returnAmount.Amount);
        }

        public void AddToPocketOne()
        {
            var returnAmount = pocketOne.Add(Amount);
            Debug.Log(returnAmount.Amount);
        }

        public void TransferFrom1To2()
        {
            var returnAmount = pocketOne.TransferTo(pocketTwo, Amount);
            Debug.Log(returnAmount);
        }

        void Update()
        {

        }
    }
}