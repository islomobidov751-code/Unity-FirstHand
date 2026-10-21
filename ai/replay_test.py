"""
Replay logged sessions through the director offline (Task 3 initial tests).

Usage:  python3 replay_test.py session_1.csv [session_2.csv ...]

For each policy (rule, and the learned model if director_model.pkl exists) it simulates the director with the
same safety rules as Unity (2 s minimum shot, never a camera that cannot see the subject) and reports:
  - switches per minute and average shot length (is it watchable, or flickering?)
  - % of time the chosen camera could see the subject (the report's visibility goal)
  - agreement with the human's camera choice on MANUAL rows
"""
import csv
import sys

from director_server import choose, load_model, visible

MIN_SHOT = 2.0


def replay(rows, model):
    current, last = "", -999.0
    switches, shots, vis_ok, agree, labeled = 0, [], 0, 0, 0
    for r in rows:
        t = float(r["Time"])
        wanted = choose(r, model, current)
        if not current:
            current, last = wanted, t
        elif wanted and wanted != current and t - last >= MIN_SHOT and visible(r, wanted):
            shots.append(t - last)
            current, last = wanted, t
            switches += 1
        vis_ok += 1 if visible(r, current) else 0
        if r.get("Mode") == "MANUAL" and r.get("ActiveCamera"):
            labeled += 1
            agree += 1 if current == r["ActiveCamera"] else 0
    minutes = max((float(rows[-1]["Time"]) - float(rows[0]["Time"])) / 60.0, 1e-6)
    return {
        "switches/min": switches / minutes,
        "avg shot (s)": sum(shots) / len(shots) if shots else float("nan"),
        "camera sees subject (%)": 100.0 * vis_ok / len(rows),
        "agrees with human (%)": 100.0 * agree / labeled if labeled else float("nan"),
    }


def main(paths):
    rows = []
    for p in paths:
        with open(p, newline="", encoding="utf-8") as fh:
            rows.extend(csv.DictReader(fh))
    if len(rows) < 10:
        sys.exit("Not enough rows.")
    model = load_model()
    results = {"rule policy": replay(rows, None)}
    if model:
        results["learned model"] = replay(rows, model)
    print("%d rows replayed\n" % len(rows))
    for name, res in results.items():
        print(name)
        for k, v in res.items():
            print("  %-26s %.1f" % (k, v))
    if not model:
        print("\n(No director_model.pkl found: train first to compare the learned model.)")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    main(sys.argv[1:])
