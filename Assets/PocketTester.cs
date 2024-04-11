using Laubrary.Pocket;
using Laubrary.Dashboard;
using Sirenix.OdinInspector;
using UnityEngine;

public class PocketTester : MonoBehaviour
{
    public Pocket<string> pocketOne = new Pocket<string>();
    public Pocket<string> pocketTwo = new Pocket<string>();

    void Start()
    {
        
    }
    public int Amount; 

    [Button]
    void RemoveFromPocketOne()
    {
        var returnAmount = pocketOne.Remove(Amount);
        Debug.Log(returnAmount.Amount);
    }

    [Button]
    void AddToPocketOne()
    {
        var returnAmount = pocketOne.Add(Amount);
        Debug.Log(returnAmount.Amount);
    }

    [Button]
    void TransferFrom1To2()
    {
        var returnAmount = pocketOne.TransferTo(pocketTwo,Amount);
        Debug.Log(returnAmount);
    }

    void Update()
    {
        
    }
}
