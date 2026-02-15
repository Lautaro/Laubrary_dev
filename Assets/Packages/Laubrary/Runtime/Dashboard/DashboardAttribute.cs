using System;
using UnityEngine;

namespace Laubrary.Dashboards
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class DashboardAttribute : Attribute
    {
        public string dashboardName;
        public int textSize;
        public Color textColor;
        public DashboardAttribute(string dashboardName = "", int textSize = 0, DashboardColor textColor = DashboardColor.Default)
        {
            this.dashboardName = dashboardName;
            this.textSize = textSize;
            this.textColor =DashboardColorMapper.MapEnumToColor(textColor);
        }
    }
}