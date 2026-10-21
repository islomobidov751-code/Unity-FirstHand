using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NorthStar
{
    /// <summary>
    /// FirstHand: finds every Interaction SDK interactor in the scene and reports, each frame, which hand is
    /// grabbing, poking or hovering. It uses reflection (any component implementing an interface named "IInteractor",
    /// reading its "State" property), so it compiles even if SDK names or namespaces differ in your version.
    /// Left/right is guessed from the object's name or its parents' names ("left"/"right").
    /// Interactor type names decide the kind: *Poke* = poke, *Grab* = grab, *Ray* = ray, *Teleport* = teleport.
    /// </summary>
    public class HandInteractionState : MonoBehaviour
    {
        enum Kind { Grab, Poke, Ray, Teleport, Group, Other }
        enum Side { Left, Right, Unknown }

        [Tooltip("Normally found automatically. Fill these only if the Console says 0 interactors were found.")]
        public MonoBehaviour[] extraLeftInteractors, extraRightInteractors;
        [Tooltip("Count a poke as active when the finger hovers, not only when it presses.")]
        public bool pokeUsesHover = false;
        public float rescanSeconds = 3f;

        // Results (updated every frame)
        public bool LeftGrab, RightGrab, LeftPoke, RightPoke, LeftRay, RightRay, LeftHover, RightHover, TeleportAiming;
        public int GrabbingHands;          // 0, 1 or 2
        public bool AnyPoke;
        public int InteractorCount;

        public string LeftState { get { return Describe(LeftGrab, LeftPoke, LeftRay, LeftHover); } }
        public string RightState { get { return Describe(RightGrab, RightPoke, RightRay, RightHover); } }

        class Probe { public MonoBehaviour mb; public PropertyInfo state; public Kind kind; public Side side; }

        readonly List<Probe> probes = new List<Probe>();
        static readonly Dictionary<Type, PropertyInfo> stateProps = new Dictionary<Type, PropertyInfo>();
        float nextScan;
        float nextDiag;
        int lastLoggedCount = -1;
        bool rescanRequested = true;

        void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
        void OnSceneLoaded(Scene s, LoadSceneMode m) { rescanRequested = true; }

        void Update()
        {
            if ((rescanRequested || probes.Count == 0) && Time.unscaledTime >= nextScan)
            {
                Scan();
                rescanRequested = false;
                nextScan = Time.unscaledTime + rescanSeconds;
            }
            Poll();
        }

        void Scan()
        {
            probes.Clear();
            MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (MonoBehaviour mb in all)
            {
                if (mb == null) continue;
                PropertyInfo p = StateProp(mb.GetType());
                if (p == null) continue;
                probes.Add(new Probe { mb = mb, state = p, kind = KindOf(mb.GetType().Name), side = SideOf(mb.transform) });
            }
            AddManual(extraLeftInteractors, Side.Left);
            AddManual(extraRightInteractors, Side.Right);
            InteractorCount = probes.Count;

            if (probes.Count == 0 && Time.unscaledTime >= nextDiag)
            {
                nextDiag = Time.unscaledTime + 15f;   // repeat every 15 s, so connecting the headset later is also reported
                var seen = new HashSet<string>();
                var sb = new System.Text.StringBuilder();
                foreach (MonoBehaviour mb in all)
                {
                    if (mb == null) continue;
                    Type t = mb.GetType();
                    if (t.Name.ToLowerInvariant().Contains("interactor") && seen.Add(t.Name))
                    {
                        sb.Append(t.Name).Append("  [interfaces: ");
                        foreach (Type i in t.GetInterfaces()) sb.Append(i.Name).Append(' ');
                        sb.Append("]\n");
                        if (seen.Count >= 8) break;
                    }
                }
                Debug.Log("HandInteractionState: 0 interactors matched. Components with 'Interactor' in their name:\n" +
                          (sb.Length > 0 ? sb.ToString() : "(none at all: the Interaction rig may not be loaded in this scene)"));
            }

            int l = 0, r = 0, u = 0;
            foreach (Probe p in probes) { if (p.side == Side.Left) l++; else if (p.side == Side.Right) r++; else u++; }
            if (probes.Count != lastLoggedCount)
            {
                lastLoggedCount = probes.Count;
                Debug.Log("HandInteractionState: found " + probes.Count + " interactors (left " + l + ", right " + r + ", unknown side " + u + ")");
            }
        }

        void AddManual(MonoBehaviour[] list, Side side)
        {
            if (list == null) return;
            foreach (MonoBehaviour mb in list)
            {
                if (mb == null) continue;
                PropertyInfo p = StateProp(mb.GetType());
                if (p == null) { Debug.LogWarning(mb.name + " does not look like an interactor (no IInteractor State)"); continue; }
                probes.RemoveAll(x => x.mb == mb);
                probes.Add(new Probe { mb = mb, state = p, kind = KindOf(mb.GetType().Name), side = side });
            }
        }

        void Poll()
        {
            LeftGrab = RightGrab = LeftPoke = RightPoke = LeftRay = RightRay = LeftHover = RightHover = TeleportAiming = false;
            bool unknownGrab = false, unknownPoke = false;
            bool leftSpec = false, rightSpec = false, anySpec = false;
            bool leftGroupSel = false, rightGroupSel = false, unknownGroupSel = false;

            foreach (Probe p in probes)
            {
                if (p.mb == null || !p.mb.isActiveAndEnabled) continue;
                string s;
                try { object v = p.state.GetValue(p.mb, null); s = v != null ? v.ToString() : ""; }
                catch { continue; }

                bool select = s == "Select", hover = s == "Hover";
                bool on = select || (p.kind == Kind.Poke && pokeUsesHover && hover);

                if (p.kind == Kind.Grab || p.kind == Kind.Poke)
                {
                    anySpec = true;
                    if (p.side == Side.Left) leftSpec = true; else if (p.side == Side.Right) rightSpec = true;
                }
                if (p.kind == Kind.Group && select)
                {
                    if (p.side == Side.Left) leftGroupSel = true; else if (p.side == Side.Right) rightGroupSel = true; else unknownGroupSel = true;
                }

                switch (p.kind)
                {
                    case Kind.Grab:
                        if (on) { if (p.side == Side.Left) LeftGrab = true; else if (p.side == Side.Right) RightGrab = true; else unknownGrab = true; }
                        break;
                    case Kind.Poke:
                        if (on) { if (p.side == Side.Left) LeftPoke = true; else if (p.side == Side.Right) RightPoke = true; else unknownPoke = true; }
                        break;
                    case Kind.Ray:
                        if (select) { if (p.side == Side.Left) LeftRay = true; else if (p.side == Side.Right) RightRay = true; }
                        break;
                    case Kind.Teleport:
                        if (select || hover) TeleportAiming = true;
                        break;
                }
                if (hover || select)
                {
                    if (p.side == Side.Left) LeftHover = true; else if (p.side == Side.Right) RightHover = true;
                }
            }

            // Fallback: if a hand has no separate grab/poke interactors, treat its whole-hand "Select" as a grab (approximation)
            if (!leftSpec && leftGroupSel) LeftGrab = true;
            if (!rightSpec && rightGroupSel) RightGrab = true;
            if (!anySpec && unknownGroupSel && !LeftGrab && !RightGrab) unknownGrab = true;

            GrabbingHands = (LeftGrab ? 1 : 0) + (RightGrab ? 1 : 0);
            if (GrabbingHands == 0 && unknownGrab) GrabbingHands = 1;
            AnyPoke = LeftPoke || RightPoke || unknownPoke;
        }

        static string Describe(bool grab, bool poke, bool ray, bool hover)
        {
            if (poke) return "POKE";
            if (grab) return "GRAB";
            if (ray) return "RAY";
            if (hover) return "HOVER";
            return "IDLE";
        }

        static PropertyInfo StateProp(Type t)
        {
            PropertyInfo p;
            if (stateProps.TryGetValue(t, out p)) return p;
            foreach (Type i in t.GetInterfaces())
                if (i.Name == "IInteractorView" || i.Name == "IInteractor")
                {
                    p = i.GetProperty("State");   // State is declared on IInteractorView, so look at both
                    if (p != null) break;
                }
            stateProps[t] = p;
            return p;
        }

        static Kind KindOf(string typeName)
        {
            string n = typeName.ToLowerInvariant();
            if (n.Contains("group")) return Kind.Group;       // e.g. BestHoverInteractorGroup: the whole hand
            if (n.Contains("poke")) return Kind.Poke;
            if (n.Contains("teleport")) return Kind.Teleport;
            if (n.Contains("ray")) return Kind.Ray;
            if (n.Contains("grab")) return Kind.Grab;
            return Kind.Other;
        }

        static Side SideOf(Transform t)
        {
            for (Transform x = t; x != null; x = x.parent)
            {
                string n = x.name.ToLowerInvariant();
                if (n.Contains("left")) return Side.Left;
                if (n.Contains("right")) return Side.Right;
            }
            return Side.Unknown;
        }
    }
}
