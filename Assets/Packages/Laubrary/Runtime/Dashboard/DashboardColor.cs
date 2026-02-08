using UnityEngine;

namespace Laubrary.Dashboards
{
    public enum DashboardColor
    {
        Default,
        red,
        green,
        blue,
        Black,
        White,
        Cyan,
        magenta,
        yellow,
        Gray
    }

    internal static class DashboardColorMapper
    {
        internal static Color MapEnumToColor(DashboardColor dashboardColor)
        {
            switch (dashboardColor)
            {
                case DashboardColor.red:
                    return new Color(1f, 0.19f, 0f); // Custom red
                case DashboardColor.green:
                    return new Color(0.35f, 0.77f, 0.13f); // Custom green
                case DashboardColor.blue:
                    return new Color(0.26f, 0.52f, 1f); // Custom blue
                case DashboardColor.Black:
                    return new Color(0f, 0f, 0f); // Black
                case DashboardColor.White:
                    return new Color(1f, 1f, 1f); // White
                case DashboardColor.Default:
                    return new Color(1f, 1f, 1f); // Default to white
                case DashboardColor.Cyan:
                    return new Color(0.42f, .8f, 1f); // Custom cyan
                case DashboardColor.magenta:
                    return new Color(0.82f, 0.11f, 0.79f); // Custom magenta
                case DashboardColor.yellow:
                    return new Color(0.83f, 0.68f, 0.15f); // Custom yellow
                case DashboardColor.Gray:
                    return new Color(0.5f, 0.5f, 0.5f); // Custom gray
                default:
                    return new Color(1f, 1f, 1f); // Default color if none is matched
            }
        }
    }
}