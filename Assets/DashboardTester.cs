using Laubrary.Dashboard;
using UnityEngine;

public class DashboardTester : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Dashboard.Log(GetInstanceID().ToString(),"> Time: " + Time.time);
    }
}
