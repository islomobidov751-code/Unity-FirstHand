using TMPro;
using UnityEngine;

namespace NorthStar
{
    /// <summary>
    /// A small board that floats in front of the player (slightly left and below eye level) and shows the viewers'
    /// messages, one at a time. Put it on an empty object called ChatBoard.
    /// Moderators can remove the message that is showing (it disappears at once) or clear the board.
    /// It reads from StreamServer. In live mode messages arrive at once and moderators remove them afterwards; the automatic helper blocks the worst ones before they show.
    /// </summary>
    public class ChatBoard : MonoBehaviour
    {
        public StreamServer server;
        [Tooltip("Used when Head is empty. Check the real name in the Hierarchy")]
        public string headName = "CenterEyeAnchor";
        public Transform head;

        [Header("Placement (meters)")]
        public float distance = 1.6f;
        public float height = -0.25f;       // below eye level
        public float sideOffset = -0.45f;   // negative = left
        public float followSmooth = 0.35f;

        [Header("Look")]
        public float width = 0.9f;
        public float boardHeight = 0.32f;
        public float fontSize = 0.6f;       // in world text units: raise it if the text is too small
        public Color background = new Color(0.04f, 0.09f, 0.13f, 0.72f);

        [Header("Timing (seconds)")]
        public float minShow = 3f;
        public float maxShow = 10f;
        public float fade = 0.35f;

        TextMeshPro label;
        Material bgMat;
        Vector3 velPos;
        float hideAt, alpha;
        bool showing;
        int currentId, seenToken;

        void Start()
        {
            if (head == null)
            {
                var h = GameObject.Find(headName);
                if (h != null) head = h.transform;
                else Debug.LogWarning("ChatBoard: no object named " + headName + ". Drag the player's head into Head.");
            }
            if (server == null) server = FindFirstObjectByType<StreamServer>();

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "Background";
            quad.transform.SetParent(transform, false);
            quad.transform.localScale = new Vector3(width + 0.06f, boardHeight + 0.04f, 1f);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.004f);   // just behind the text
            bgMat = new Material(Shader.Find("Sprites/Default"));
            quad.GetComponent<Renderer>().sharedMaterial = bgMat;

            var go = new GameObject("Text");
            go.transform.SetParent(transform, false);
            label = go.AddComponent<TextMeshPro>();
            label.rectTransform.sizeDelta = new Vector2(width, boardHeight);
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.richText = true;
            label.color = Color.white;
            label.overflowMode = TextOverflowModes.Ellipsis;
            SetAlpha(0f);
            if (head != null) transform.position = head.position + head.forward * distance;
        }

        void SetAlpha(float a)
        {
            alpha = a;
            if (label != null) label.alpha = a;
            if (bgMat != null) { var c = background; c.a = background.a * a; bgMat.color = c; }
        }

        void Update()
        {
            if (server == null || label == null) return;
            // a moderator removed the message that is on the board, or cleared the board: hide it at once
            if (showing && (server.IsRemoved(currentId) || server.BoardClearToken != seenToken)) { showing = false; hideAt = 0f; }
            if (!showing || Time.time >= hideAt)
            {
                string n, t; bool mod; int count, id;
                if (server.TryTakeForPlayer(out id, out n, out t, out mod, out count))
                {
                    currentId = id; seenToken = server.BoardClearToken;
                    string who = "<color=#" + (mod ? "ffcc33" : "7fd4ff") + "><b>" + n + "</b></color>" + (count > 1 ? " <size=70%>x" + count + "</size>" : "");
                    label.text = who + "\n" + t;
                    hideAt = Time.time + Mathf.Clamp(2f + t.Length * 0.06f, minShow, maxShow);
                    showing = true;
                }
                else showing = false;
            }
            float target = showing ? 1f : 0f;
            SetAlpha(Mathf.MoveTowards(alpha, target, Time.deltaTime / Mathf.Max(0.05f, fade)));
        }

        void LateUpdate()
        {
            if (head == null) return;
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 target = head.position + fwd * distance + Vector3.up * height + right * sideOffset;
            transform.position = Vector3.SmoothDamp(transform.position, target, ref velPos, followSmooth);
            Quaternion want = Quaternion.LookRotation(transform.position - head.position, Vector3.up);   // text faces the player
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }

        void OnDestroy() { if (bgMat != null) Destroy(bgMat); }
    }
}
