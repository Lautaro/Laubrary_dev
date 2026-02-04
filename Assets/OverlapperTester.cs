using Laubrary.Dashboard;
using Laubrary.Overlapper2D;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OverlapperTester : MonoBehaviour
{
    public Overlapper2D overlapper2D;
    public LayerMask layerMask;
    // Start is called before the first frame update
    void Start()
    {
        overlapper2D = GetComponent<Overlapper2D>();
    }

    // Update is called once per frame
    void Update()
    {
        var overlaps = overlapper2D.GetTriggers();
        string info = "";

        if (overlaps != null)
        {
            foreach (var item in overlaps)
            {
                info += item.name + " ";
            }
        }

        Dashboard.QuickLog("OverlapInfo", info, 20, DashboardColor.green);
    }
}
