using Laubrary.Dashboard;
using UnityEngine;
using UnityEngine.UI;

public class DashboardTester : MonoBehaviour
{
    [Dashboard("Health",12,DashboardColor.magenta)]
    public int lifeForce;

    [Dashboard()]
    public string textMessage;

    public Button ClickMe;

    void Update()
    {
        this.DashboardLog("Hello i am : " + textMessage, 9, DashboardColor.blue);

        Dashboard.QuickLog(this.GetInstanceID().ToString(), textMessage + "private", 18, DashboardColor.blue);

        ClickMe.onClick.AddListener(() => {
            Dashboard.QuickLog("Button is clicked");
        });
        
        //Dashboard.QuickLog("Testy",DashboardColor.red);
        //Dashboard.QuickLog("Id2", textMessage, 18, DashboardColor.magenta);
        //Dashboard.QuickLog("Id3", textMessage, 18, DashboardColor.yellow);
        //Dashboard.QuickLog("Id4", textMessage, 18, DashboardColor.red);
        //Dashboard.QuickLog("id5", textMessage, 18, DashboardColor.green);
    }
}
