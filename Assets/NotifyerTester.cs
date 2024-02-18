using Laubrary.Notifyer;
using Lautaro.Cookbook;
using UnityEngine;

public class NotifyerTester : MonoBehaviour
{
    public string MyMessage = "To you RUDY!";
    // Start is called before the first frame update
    void Start()
    {
        Notifyer.AddReference(this, "TesterObject");
    }

    // Update is called once per frame
    void Update()
    {
        if (KeyCode.Space.GetKeyDown())
        {
            var tester = Notifyer.Reference<NotifyerTester>("TesterObject");
            Debug.Log(tester.MyMessage);

        }
    }
}
