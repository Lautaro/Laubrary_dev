#!/bin/bash
DN="C:/Program Files/Unity/Hub/Editor/6000.4.6f1/Editor/Data/NetCoreRuntime/dotnet.exe"
CSC="C:/Program Files/Unity/Hub/Editor/6000.4.6f1/Editor/Data/DotNetSdkRoslyn/csc.dll"
cd "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0109/verify"
"$DN" "$CSC" -nologo -target:exe -out:probe2.dll -langversion:latest -unsafe -nowarn:0219,0414,0169,0649 @refs.rsp Stubs.cs ZUIValueStub.cs Main1.cs March.cs March2.cs Tests.cs Tests2.cs W2.cs ILB.cs F2.cs F1.cs F3.cs IUB.cs V2.cs V2b.cs V2c.cs V2d.cs V2e.cs V2f.cs V2g.cs -main:Main2 @src.rsp 2>&1 | grep -i "error" | head -15
