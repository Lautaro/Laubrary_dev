var results = new System.Collections.Generic.List<string>();
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) { try { foreach (var t in assembly.GetTypes()) if (t.Name.Contains("FrozenBackSplash") || t.Name == "FoundationWindow") results.Add(t.FullName + " | " + assembly.FullName); } catch (System.Reflection.ReflectionTypeLoadException) {} }
return string.Join("\n", results);
