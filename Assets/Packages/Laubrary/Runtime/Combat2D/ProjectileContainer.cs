using UnityEngine;

namespace Laubrary.Combat2D
{
    /// <summary>
    /// One shared, stable parent every in-flight projectile lives under — deliberately NOT the shooter (a
    /// shooter dying, or a weapon slot deactivating on switch, must not delete/hide bullets it already
    /// fired) and NOT scene root (churn there jitters the Hierarchy as bullets come and go rapidly). One
    /// stable node absorbs that churn instead.
    /// </summary>
    public static class ProjectileContainer
    {
        static Transform _root;

        public static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var go = new GameObject("~Projectiles");
                    Object.DontDestroyOnLoad(go);
                    _root = go.transform;
                }
                return _root;
            }
        }
    }
}
