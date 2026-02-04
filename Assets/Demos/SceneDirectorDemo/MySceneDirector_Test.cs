using Laubrary.SceneDirector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MySceneDirector_Test : SceneDirector
{
    [Directable(StartState.Disabled)] public SD_TestScriptOne SD_TestScriptOne;
    [Directable(StartState.Enabled)] public SD_TestScriptOne SD_TestScriptOneB;
    [Directable("LoadingComplete")] public SD_AnotherTestScript SD_AnotherTestScript;
    [Directable("StartTheAction")] public SD_AnotherTestScript SD_AnotherTestScriptB;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }
}
