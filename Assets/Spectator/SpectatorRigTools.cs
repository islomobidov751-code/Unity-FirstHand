#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NorthStar
{
    /// <summary>
    /// Menu items that repeat the same setup in every level (Tools > Spectator). Put this file in Assets/Scripts/Spectator/Editor.
    ///   1 Reduce To 5 Cameras      keeps 5 player cameras, moves the rest into an inactive UnusedCameras object
    ///   2 Add Fly Pool And Zoom    adds the personal fly cameras and the auto zoom, and connects them to StreamServer
    ///   3 Check Rig                prints what is set up in the open scene
    ///   4 Add Chat Board           adds the floating chat board for the player and connects it to StreamServer
    /// They work on the open scenes and can be undone with Ctrl+Z.
    /// </summary>
    public static class SpectatorRigTools
    {
        static readonly string[] Remove =
        {
            "RAW_FPV", "CROPPED_FPV", "OTS_HAND_ALT", "POSE_HANDS", "DYNAMIC_TRACKING",
            "ORBIT_TWO_HAND", "WIDE_FOLLOW", "WIDE_SCENE_FIXED", "FREE_ORBIT", "FREE_FLY"
        };
        static readonly string[] Keep = { "PLAYER_STABILIZED", "TORSO_ANCHORED", "OTS_HAND", "OTS_CLOSE", "CLOSE_BOTH_HANDS" };

        static Transform FindByName(string name)   // finds inactive objects too
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            return null;
        }

        static void Dirty() { EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); }

        [MenuItem("Tools/Spectator/1 Reduce To 5 Cameras")]
        static void Reduce()
        {
            Transform rig = FindByName("SpectatorCameras");
            if (rig == null) { Debug.LogWarning("Spectator: no SpectatorCameras in the active scene. Run Tools > Spectator > Create FirstHand Cameras first."); return; }
            Transform unused = FindByName("UnusedCameras");
            if (unused == null)
            {
                var go = new GameObject("UnusedCameras");
                Undo.RegisterCreatedObjectUndo(go, "Reduce cameras");
                go.SetActive(false);
                unused = go.transform;
            }
            var move = new List<Transform>();
            foreach (Transform child in rig)
                if (System.Array.IndexOf(Remove, child.name) >= 0) move.Add(child);
            foreach (var t in move) Undo.SetTransformParent(t, unused, "Reduce cameras");
            Dirty();
            var missing = new List<string>();
            foreach (var k in Keep) if (FindByName(k) == null) missing.Add(k);
            Debug.Log("Spectator: moved " + move.Count + " cameras into UnusedCameras (inactive)." +
                      (missing.Count > 0 ? " NOT FOUND: " + string.Join(", ", missing) : " All 5 kept cameras are present."));
        }

        [MenuItem("Tools/Spectator/2 Add Fly Pool And Zoom")]
        static void AddFlyAndZoom()
        {
            var server = UnityEngine.Object.FindFirstObjectByType<StreamServer>();
            if (server == null) { Debug.LogWarning("Spectator: no StreamServer in the open scenes. Copy ControlRoom into this scene first."); return; }

            var pool = UnityEngine.Object.FindFirstObjectByType<FlyCameraPool>();
            if (pool == null)
            {
                var go = new GameObject("FlyPool");
                Undo.RegisterCreatedObjectUndo(go, "Add fly pool");
                pool = Undo.AddComponent<FlyCameraPool>(go);
            }
            Undo.RecordObject(pool, "Set fly pool");
            pool.templateName = "OTS_HAND";          // any kept camera: only its render settings are copied
            Undo.RecordObject(server, "Connect fly pool");
            server.flyPool = pool;

            var zoom = server.GetComponent<ZoomDirector>();
            if (zoom == null) zoom = Undo.AddComponent<ZoomDirector>(server.gameObject);
            Undo.RecordObject(zoom, "Set zoom logger");
            zoom.logger = server.logger;

            EditorUtility.SetDirty(server); EditorUtility.SetDirty(pool); EditorUtility.SetDirty(zoom);
            Dirty();
            Debug.Log("Spectator: FlyPool connected to StreamServer, ZoomDirector added. Logger set: " + (zoom.logger != null));
        }

        [MenuItem("Tools/Spectator/4 Add Chat Board")]
        static void AddChatBoard()
        {
            var server = UnityEngine.Object.FindFirstObjectByType<StreamServer>();
            if (server == null) { Debug.LogWarning("Spectator: no StreamServer in the open scenes. Copy ControlRoom into this scene first."); return; }
            var board = UnityEngine.Object.FindFirstObjectByType<ChatBoard>();
            if (board == null)
            {
                var go = new GameObject("ChatBoard");
                Undo.RegisterCreatedObjectUndo(go, "Add chat board");
                board = Undo.AddComponent<ChatBoard>(go);
            }
            Undo.RecordObject(board, "Connect chat board");
            board.server = server;
            EditorUtility.SetDirty(board);
            Dirty();
            Debug.Log("Spectator: ChatBoard connected to StreamServer. Set the moderator key with the environment variable FIRSTHAND_MOD_KEY (6+ characters)." + (!server.ModKeySet ? " (the moderator key is not set yet)" : ""));
        }

        [MenuItem("Tools/Spectator/3 Check Rig")]
        static void Check()
        {
            Transform rig = FindByName("SpectatorCameras");
            var names = new List<string>();
            if (rig != null) foreach (Transform c in rig) names.Add(c.name);
            var server = UnityEngine.Object.FindFirstObjectByType<StreamServer>();
            var pool = UnityEngine.Object.FindFirstObjectByType<FlyCameraPool>();
            string anchor = pool != null ? pool.anchorName : "CenterEyeAnchor";
            Debug.Log("Spectator check for scene '" + SceneManager.GetActiveScene().name + "':\n" +
                      "  SpectatorCameras: " + (rig != null ? names.Count + " children (" + string.Join(", ", names) + ")" : "MISSING") + "\n" +
                      "  StreamServer: " + (server != null ? "found" : "MISSING") + (server != null && server.logger == null ? " (logger empty!)" : "") + "\n" +
                      "  FlyPool: " + (pool != null ? "found" : "MISSING") + ", connected: " + (server != null && server.flyPool == pool && pool != null) + "\n" +
                      "  ChatBoard: " + (UnityEngine.Object.FindFirstObjectByType<ChatBoard>() != null ? "found" : "MISSING") + (server != null && !server.ModKeySet ? ", moderator key NOT SET" : "") + "\n" +
                      "  ZoomDirector: " + (UnityEngine.Object.FindFirstObjectByType<ZoomDirector>() != null ? "found" : "MISSING") + "\n" +
                      "  Player anchor '" + anchor + "': " + (GameObject.Find(anchor) != null ? "found" : "NOT FOUND (fix Anchor Name on FlyPool)"));
        }
    }
}
#endif
