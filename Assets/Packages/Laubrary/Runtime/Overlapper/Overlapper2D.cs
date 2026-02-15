using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Laubrary.Overlapper
{
    public class Overlapper2D : MonoBehaviour
    {
        List<Collision2D> collisionStay = new List<Collision2D>();
        List<Collider2D> triggerStay = new List<Collider2D>();

        void OnCollisionEnter2D(Collision2D collision)
        {
            collisionStay.Add(collision);
        }

        void OnCollisionExit2D(Collision2D collision)
        {
            collisionStay.Remove(collision);
        }


        void OnTriggerEnter2D(Collider2D collider)
        {
            triggerStay.Add(collider);
        }

        void OnTriggerExit2D(Collider2D collider)
        {
            triggerStay.Remove(collider);
        }

        public List<GameObject> GetTriggers(LayerMask? layerMask = null)
        {
            if (triggerStay.Count > 0)
            {
                if (layerMask != null)
                {
                    var filteredTriggers = layerMask.HasValue
                        ? triggerStay.Where(t => ((1 << t.gameObject.layer) & layerMask.Value) != 0)
                        : triggerStay;

                    var gos = filteredTriggers.Select(t => t.gameObject).ToList();
                    return gos;
                }
                else
                {
                    return triggerStay.Select(ts=> ts.gameObject).ToList();
                }
            }

            return new List<GameObject>();
        }

        /// <summary>True if the id is the gameobject name or the tag of either a collision or a trigger overlapping at the moment.</summary>
        public bool Check(string id)
        {
            foreach (var collider in triggerStay)
            {
                if (collider.name == id || collider.tag == id)
                {
                    return true;
                }
            }

            foreach (var collision in collisionStay)
            {
                if (collision.gameObject.name == id || collision.gameObject.tag == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if the id is the gameobject tag of either a collision or a trigger overlapping at the moment.</summary>
        public bool ByTag(string tag)
        {
            foreach (var collider in triggerStay)
            {
                if (collider.tag == tag)
                    return true;
            }

            foreach (var collision in collisionStay)
            {
                if (collision.gameObject.tag == tag)
                    return true;
            }

            return false;
        }

        /// <summary>True if the id is the name of either a collision or a trigger overlapping at the moment.</summary>
        public bool ByName(string name)
        {
            foreach (var collider in triggerStay)
            {
                if (collider.name == name)
                    return true;
            }

            foreach (var collision in collisionStay)
            {
                if (collision.gameObject.name == name)
                    return true;
            }

            return false;
        }

        /// <summary>True if the id is the name or tag of a trigger overlapping at the moment.</summary>
        public Collider2D Trigger(string id)
        {
            foreach (var collider in triggerStay)
            {
                if (collider.name == id || collider.tag == id)
                {
                    return collider;
                }
            }
            return null;
        }

        /// <summary>True if the id is the name of a gameobject of a colllision overlapping at the moment.</summary>
        public Collision2D Collision(string id)
        {
            foreach (var collision in collisionStay)
            {
                if (collision.gameObject.name == id || collision.gameObject.tag == id)
                {
                    return collision;
                }
            }
            return null;
        }

        ///<summary>True if the id is the gameobject name or the tag of either a collision or a trigger overlapping at the moment. 
        ///Also provides an out variable with an OverlapInfo object with all information. </summary>
        public bool Check(string id, out OverlapInfo info)
        {
            info = null;
            foreach (var collider in triggerStay)
            {
                if (collider.name == id || collider.tag == id)
                {
                    info = new OverlapInfo(collider, collider.tag == id, collider.name == id);
                    return true;
                }
            }

            foreach (var collision in collisionStay)
            {
                if (collision.gameObject.name == id || collision.gameObject.tag == id)
                {
                    info = new OverlapInfo(collision, collision.gameObject.tag == id, collision.gameObject.name == id);
                    return true;
                }
            }

            return false;
        }

        public GameObject GetTrigger(string id)
        {
            return triggerStay.FirstOrDefault(t => t.name == id || t.CompareTag(id)).gameObject;
        }
    }

    public class OverlapInfo
    {
        public GameObject gameObject;
        public bool isCollider;
        public bool isTrigger;
        public bool byTag;
        public bool byName;
        public Collision2D collision;
        public Collider2D collider;
        public string name => gameObject.name;
        public string tag => gameObject.tag;

        public OverlapInfo(Collider2D collider, bool byTag, bool byName)
        {
            this.byTag = byTag;
            this.byName = byName;

            gameObject = collider.gameObject;
            isCollider = true;
            this.collider = collider;
        }

        public OverlapInfo(Collision2D collision, bool byTag, bool byName)
        {
            this.byTag = byTag;
            this.byName = byName;

            gameObject = collision.gameObject;
            isTrigger = true;
            this.collision = collision;
        }
    }
    public static class OverlapExtensions
    {
        /// <summary>True if the id is the tag of either a collision or a trigger overlapping at the moment.</summary>
        public static bool OverlapTag(this GameObject go, string tag)
        {
            return GetOverlap(go).ByTag(tag);
        }
        /// <summary>True if the id is the gameobject name of either a collision or a trigger overlapping at the moment.</summary>
        public static bool OverlapName(this GameObject go, string name)
        {
            return GetOverlap(go).ByName(name);
        }

        /// <summary>True if the id is the gameobject name or tag of either a collision or a trigger overlapping at the moment.</summary>
        public static bool Overlap(this GameObject go, string id)
        {
            var overlap = GetOverlap(go);
            return overlap.ByTag(id) || overlap.ByName(id);
        }

        ///<summary>True if the id is the gameobject name or the tag of either a collision returns an OverlapInfo object with all information. If no trigger or collision exists null is returned. </summary>
        public static OverlapInfo OverlapInfo(this GameObject go, string id)
        {
            var overlap = GetOverlap(go);
            if (overlap.Check(id, out var overlapInfo))
            {
                return overlapInfo;
            }

            return null;
        }

        static Overlapper2D GetOverlap(GameObject go)
        {
            var overlap = go.GetComponent<Overlapper2D>();
            if (overlap == null)
            {
                Debug.Log("Overlapper2D is being called on a gameobject that has no Overlapper2D component. Overlapper2D is being added. ");
                go.AddComponent<Overlapper2D>();
                overlap = go.AddComponent<Overlapper2D>();
            }

            return overlap;
        }
    }

}