#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Laubrary.Loom;
using Laubrary.Daemon;

namespace Laubrary.Demos.DaemonDemo
{
    // Authors the DaemonDemo's Brain + BehaviourSet as real .asset files so the shared Loom graph window
    // ("Laubrary/Brain Graph") has something to open, drag and rewire. The runtime demo (DaemonEnemyDemo) can
    // build the same brain in code; this is the *authored* equivalent — the thing a game ships per enemy type.
    //
    // The graph laid out here (left→right):  Entry → Status "Spawn" → Wait[hasTarget] → Seek ⇄ Shoot
    //   Seek  parks, walking toward the target, until "inRange"     → Shoot
    //   Shoot parks, firing the weapon,          until "outOfRange" → Seek
    // Conditions (hasTarget/inRange/outOfRange) are answered by ColosseumAgentBody.Evaluate.
    public static class DaemonDemoBrain
    {
        const string Dir = "Assets/Demos/DaemonDemo";
        const string BrainPath = Dir + "/DemoBrain.asset";
        const string SetPath   = Dir + "/DemoBehaviours.asset";

        [MenuItem("Laubrary/Demos/Build Daemon Demo Brain")]
        public static void Build()
        {
            // Create-if-missing: never clobber a brain the user has since tweaked in the graph window.
            var brain = AssetDatabase.LoadAssetAtPath<Brain>(BrainPath);
            if (brain != null)
            {
                Debug.Log($"[DaemonDemo] Brain already exists at {BrainPath} — selecting it (delete it first to rebuild).");
                Select(brain);
                return;
            }

            var behaviours = BuildBehaviourSet();
            AssetDatabase.CreateAsset(behaviours, SetPath);

            brain = BuildBrain();
            AssetDatabase.CreateAsset(brain, BrainPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[DaemonDemo] Built {BrainPath} ({brain.Nodes.Count} nodes, {brain.Edges.Count} edges) + {SetPath}. " +
                      "Open it via the asset's \"Open in Brain Graph\" button or Laubrary/Brain Graph with it selected.");
            Select(brain);
        }

        static Brain BuildBrain()
        {
            var brain = ScriptableObject.CreateInstance<Brain>();

            var entry  = new EntryNode        { Id = "entry",  Title = "Start",         GraphPos = new Vector2( 40, 200) };
            var status = new SetStatusNode    { Id = "status", Label = "Spawn",         GraphPos = new Vector2(240, 200) };
            var wait   = new WaitConditionNode{ Id = "wait",   Condition = "hasTarget", GraphPos = new Vector2(460, 200) };
            var seek   = new StateNode        { Id = "seek",   BehaviourId = "SeekBehaviour",  GraphPos = new Vector2(700, 100),
                Transitions = new List<Transition> { new Transition { Port = "toShoot", Condition = "inRange" } } };
            var shoot  = new StateNode        { Id = "shoot",  BehaviourId = "ShootBehaviour", GraphPos = new Vector2(700, 320),
                Transitions = new List<Transition> { new Transition { Port = "toSeek", Condition = "outOfRange" } } };

            brain.Nodes = new List<BrainNode> { entry, status, wait, seek, shoot };
            brain.Edges = new List<Edge>
            {
                new Edge("entry",  "out",     "status"),
                new Edge("status", "out",     "wait"),
                new Edge("wait",   "out",     "seek"),
                new Edge("seek",   "toShoot", "shoot"),
                new Edge("shoot",  "toSeek",  "seek"),
            };
            brain.EntryId = "entry";
            return brain;
        }

        static BehaviourSet BuildBehaviourSet()
        {
            var set = ScriptableObject.CreateInstance<BehaviourSet>();
            set.Behaviours = new List<AgentBehaviour> { new SeekBehaviour(), new ShootBehaviour() };
            return set;
        }

        static void Select(Object o)
        {
            Selection.activeObject = o;
            EditorGUIUtility.PingObject(o);
        }
    }
}
#endif
