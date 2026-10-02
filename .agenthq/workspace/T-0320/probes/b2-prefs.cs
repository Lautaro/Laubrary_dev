UnityEditor.EditorPrefs.SetString("T320.capTag","T320Tag");
UnityEditor.EditorPrefs.SetString("T320.capMode", UnityEditor.EditorPrefs.GetString("T320.mode","screen"));
UnityEditor.EditorPrefs.SetString("T320.capOut", UnityEditor.EditorPrefs.GetString("T320.out", @"C:\Users\Lauta\AppData\Local\Temp\claude\D--UNITY-Laubrary-Dev---Shaper\b6197aa9-96a6-43f3-921b-83cf03d08706\scratchpad\shots\t320.png"));
return "prefs set mode=" + UnityEditor.EditorPrefs.GetString("T320.capMode");
