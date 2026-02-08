using UnityEngine;


namespace Laubrary.Cookbook2D.Samples
{
    public class PolygonRandomPointTester : MonoBehaviour
    {
        PolygonCollider2D polly;
        public GameObject player;


        void Start()
        {
            polly = GetComponent<PolygonCollider2D>();

        }

        private void MovePlayerInsidePolygon()
        {
            if (polly == null)
                polly = GetComponent<PolygonCollider2D>();
            player.transform.position = polly.GetRandomPoint();
        }

        // Update is called once per frame
        void Update()
        {

        }
    }

}