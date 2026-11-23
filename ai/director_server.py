"""
AI camera director server (works for FirstHand)

Run:  python3 director_server.py
Unity (AIDirectorBridge.cs) sends the logger's latest values as  current=CAM;Name=value;Name=value;...
This server replies with ONE camera name. Cameras are discovered from the Vis_<camera> columns.

Policy:
  - If director_model.pkl exists (made by train_director.py): the learned model picks the camera it rates best
    among the cameras that can currently see the subject.
  - Otherwise: a rule policy (FirstHand events).
Safety rules (minimum shot time, visibility check) are also enforced again in Unity.
"""
import os
import pickle
import socket

LISTEN = ("127.0.0.1", 5005)
UNITY = ("127.0.0.1", 5006)
MODEL_FILE = "director_model.pkl"

FIRSTHAND_PREFS = {
    "REESTABLISH": ["WIDE_FOLLOW", "WIDE_SCENE_FIXED"],
    "GRAB_TWO_HAND": ["CLOSE_BOTH_HANDS", "POSE_HANDS", "ORBIT_TWO_HAND"],
    "INTERACT_POKE": ["OTS_CLOSE", "OTS_HAND"],
    "GRAB_ONE_HAND": ["OTS_HAND", "OTS_HAND_ALT"],
    "IDLE": ["PLAYER_STABILIZED", "TORSO_ANCHORED", "WIDE_FOLLOW"],
}

# --- Variety Rule Globals ---
CAMERA_HISTORY = []      # Tracks camera usage order (oldest used at index 0)
LAST_EVENT = None        # Tracks the previous frame's event to prevent flickering
VARIETY_THRESHOLD = 0.15 # Max probability difference to count as "almost equally good"


def parse(msg):
    d = {}
    for part in msg.split(";"):
        if "=" in part:
            k, v = part.split("=", 1)
            d[k.strip()] = v.strip()
    return d


def num(x, default=0.0):
    try:
        return float(x)
    except (TypeError, ValueError):
        return default


def cameras(d):
    return [k[4:] for k in d if k.startswith("Vis_")]


def visible(d, cam):
    return d.get("Vis_" + cam, "1") != "0"


def first_visible(d, names, cams):
    for n in names:
        if n in cams and visible(d, n):
            return n
    return None


def rule_choice(d, current=""):
    cams = cameras(d)
    event = d.get("Event", "")
    pick = None
    if event in FIRSTHAND_PREFS:
        pick = first_visible(d, FIRSTHAND_PREFS[event], cams)
    elif "Steering" in d:
        s = num(d["Steering"])
        key = "RIGHT" if s > 0.5 else "LEFT" if s < -0.5 else "STRAIGHT"
        pick = first_visible(d, NORTHSTAR_PREFS[key], cams)
    if pick:
        return pick
    if current in cams and visible(d, current):
        return current
    for c in cams:
        if visible(d, c):
            return c
    return current


def load_model(path=MODEL_FILE):
    if os.path.exists(path):
        with open(path, "rb") as fh:
            return pickle.load(fh)
    return None


def vector(d, m):
    return [num(d.get(c)) for c in m["columns"]] + [1.0 if d.get("Event", "") == e else 0.0 for e in m["events"]]


def model_choice(d, m, current=""):
    global LAST_EVENT, CAMERA_HISTORY
    
    cams = cameras(d)
    event = d.get("Event", "")
    
    # 1. No Flicker Rule: If the event hasn't changed, stick to the current camera
    if event == LAST_EVENT and current in cams and visible(d, current):
        return current
    
    LAST_EVENT = event
    
    # Get model probabilities
    proba = m["clf"].predict_proba([vector(d, m)])[0]
    classes = list(m["clf"].classes_)
    
    # 2. Gather scores for all valid, visible cameras
    valid_candidates = {}
    for c in cams:
        if c in classes and visible(d, c):
            p = proba[classes.index(c)] + (0.05 if c == current else 0.0)  # stickiness
            valid_candidates[c] = p
            
    if not valid_candidates:
        return rule_choice(d, current)
        
    # 3. Find the absolute best probability score
    max_p = max(valid_candidates.values())
    
    # 4. Filter cameras that are "almost equally good" (within VARIETY_THRESHOLD of the top score)
    equally_good = [
        cam for cam, p in valid_candidates.items() 
        if (max_p - p) <= VARIETY_THRESHOLD
    ]
    
    # 5. Out of the top choices, pick the one used LEAST recently
    if len(equally_good) == 1:
        best = equally_good[0]
    else:
        # Lower index means older. If it's never been used, assign -1 so it gets picked first.
        best = min(
            equally_good, 
            key=lambda c: CAMERA_HISTORY.index(c) if c in CAMERA_HISTORY else -1
        )
        
    # 6. Update the historical tracking list
    if best in CAMERA_HISTORY:
        CAMERA_HISTORY.remove(best)
    CAMERA_HISTORY.append(best)
    
    return best


def choose(d, model=None, current=""):
    return model_choice(d, model, current) if model else rule_choice(d, current)


def main():
    model = load_model()
    print("Loaded " + MODEL_FILE + " (learned policy)" if model else "No model file: using the rule policy")
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(LISTEN)
    print("Listening on %s:%d. Press Ctrl+C to stop." % LISTEN)
    last = None
    while True:
        data, _ = sock.recvfrom(65535)
        try:
            d = parse(data.decode("utf-8"))
            cam = choose(d, model, d.get("current", ""))
        except Exception as e:
            print("Bad message, ignoring:", e)
            continue
        if cam:
            sock.sendto(cam.encode("utf-8"), UNITY)
            if cam != last:
                print("->", cam, "  (event=%s)" % d.get("Event", "-"))
                last = cam


if __name__ == "__main__":
    main()
