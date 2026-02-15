using Laubrary.Dashboards;
using UnityEngine;


namespace Laubrary.Overlapper.Samples
{
    public class OverlapperTester : MonoBehaviour
    {
        public Overlapper2D overlapper;
        public LayerMask layerMask;
        // Start is called before the first frame update
        void Start()
        {
            overlapper = GetComponent<Overlapper2D>();
        }

        // Update is called once per frame
        void Update()
        {
            var overlaps = overlapper.GetTriggers();
            string info = "";

            if (overlaps != null)
            {
                foreach (var item in overlaps)
                {
                    info += item.name + " ";
                }
            }

            Dashboards.Dashboard.QuickLog("OverlapInfo", info, 20, DashboardColor.green);
        }
    }

}