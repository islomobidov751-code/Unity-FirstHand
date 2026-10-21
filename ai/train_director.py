"""
Train the director from logged sessions.

Usage:  python3 train_director.py session_1.csv [session_2.csv ...]
        (CSV files from the dashboard's Sessions folder, same game)

Labels = the camera a HUMAN chose. Rows where Mode == MANUAL use ActiveCamera as the label,
or you can add a BestCamera column by hand (e.g. from the viewer-rating checklist).
So: operate the dashboard in MANUAL mode while playing or replaying, and the logs become training data.

Evaluation uses a time-aware split (last session, or the last 30% of rows) because neighbouring rows are
almost identical; a random split would make the results look much better than they are.
"""
import pickle
import sys

import numpy as np
import pandas as pd
from sklearn.ensemble import RandomForestClassifier

from director_server import MODEL_FILE

IGNORE = {"Time", "Frame", "FPS", "Mode", "ActiveCamera", "BestCamera", "Scene", "Player", "Event"}


def main(paths):
    frames = []
    for i, p in enumerate(paths):
        df = pd.read_csv(p)
        df["_session"] = i
        frames.append(df)
    df = pd.concat(frames, ignore_index=True)

    if "BestCamera" in df.columns and df["BestCamera"].notna().any():
        y = df["BestCamera"]
    elif "Mode" in df.columns and "ActiveCamera" in df.columns:
        y = df["ActiveCamera"].where(df["Mode"] == "MANUAL")
    else:
        sys.exit("No labels found: need a BestCamera column, or rows with Mode == MANUAL.")

    keep = y.notna()
    df, y = df[keep].reset_index(drop=True), y[keep].reset_index(drop=True)
    if len(df) < 30:
        sys.exit("Only %d labeled rows. Record more MANUAL-mode data first." % len(df))
    print("Labeled rows: %d" % len(df))
    print(y.value_counts().to_string())

    cols = []
    for c in df.columns:
        if c in IGNORE or c.startswith("_"):
            continue
        df[c] = pd.to_numeric(df[c], errors="coerce")
        if df[c].notna().mean() > 0.9:
            cols.append(c)
    df[cols] = df[cols].fillna(0.0)

    events = sorted(df["Event"].dropna().unique()) if "Event" in df.columns else []
    ev = df["Event"] if "Event" in df.columns else pd.Series([""] * len(df))
    X = np.hstack([df[cols].values, np.array([[1.0 if e == v else 0.0 for v in events] for e in ev]).reshape(len(df), len(events))])

    sessions = df["_session"].unique()
    if len(sessions) >= 2:
        test = (df["_session"] == sessions.max()).values
    else:
        test = np.arange(len(df)) >= int(len(df) * 0.7)
    train = ~test
    if train.sum() == 0 or test.sum() == 0:
        sys.exit("Train or test set is empty.")

    clf = RandomForestClassifier(n_estimators=200, max_depth=8, class_weight="balanced", random_state=0)
    clf.fit(X[train], y[train])
    acc_model = (clf.predict(X[test]) == y[test]).mean()

    majority = y[train].value_counts().index[0]
    acc_major = (y[test] == majority).mean()
    by_event = pd.DataFrame({"e": ev[train].values, "y": y[train].values}).groupby("e")["y"].agg(lambda s: s.value_counts().index[0])
    pred_rule = ev[test].map(by_event).fillna(majority)
    acc_rule = (pred_rule.values == y[test].values).mean()

    print("\nAgreement with the human's choice on unseen rows (%d train / %d test):" % (train.sum(), test.sum()))
    print("  always the most common camera : %.2f" % acc_major)
    if events:
        print("  best camera per event (rule)  : %.2f" % acc_rule)
    print("  learned model                 : %.2f" % acc_model)
    print("Agreement with a human is not the same as viewer quality. Check with replay_test.py and the viewer test.")

    with open(MODEL_FILE, "wb") as fh:
        pickle.dump({"clf": clf, "columns": cols, "events": events}, fh)
    print("Saved " + MODEL_FILE)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    main(sys.argv[1:])
