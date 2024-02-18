using Laubrary.Dashboard;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AttributeTester : MonoBehaviour
{

    [Dashboard("MyField")]
    public string myField;

    [Dashboard("Second Field")]
    public string myField2;


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        myField = transform.position.ToString();
        myField2 = name;
        
    }
}
