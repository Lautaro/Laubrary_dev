using Laubrary.Dashboard;
using Laubrary.Overlapper2D;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OverlapperTester : MonoBehaviour
{
    public Overlapper2D overlapper2D;
    // Start is called before the first frame update
    void Start()
    {
        overlapper2D = GetComponent<Overlapper2D>();
    }

    // Update is called once per frame
    void Update()
    {
        var overlapsArea52 = overlapper2D.ByName("Area52");
        var overlapsTriangle = overlapper2D.ByName("Triangle");
        Dashboard.QuickLog("Are52", "Area52 : " + overlapsArea52, 20, DashboardColor.green);
        Dashboard.QuickLog("Triangle", "Triangle : " + overlapsTriangle, 20, DashboardColor.yellow);
    }
}
