"""
ElanAP Replay & Console Log Analyzer
=====================================
Dùng để hiệu chuẩn touchpad mania driver.

Cần: pip install osrparse

Cách dùng:
  1. Đặt file .osr (replay) vào cùng thư mục
  2. Đặt file .osu (beatmap) vào cùng thư mục (tùy chọn, nhưng cần để tính hit error)
  3. Đặt console log vào file console.log (tùy chọn)
  4. Chạy: python analyze_replay.py
"""

import re
import statistics
from pathlib import Path
from osrparse import Replay


SCRIPT_DIR = Path(__file__).parent


# ===========================================================================
#  PHẦN 1: Parse beatmap .osu để lấy note timing
# ===========================================================================

def parse_osu_mania_notes(osu_path, key_count):
    """
    Parse file .osu, trả về list of (time_ms, column, is_long_note, end_time_ms).
    column: 0-based, tính từ x position trong file .osu.
    """
    notes = []
    in_hitobjects = False
    col_width = 512 // key_count

    with open(osu_path, "r", encoding="utf-8-sig") as f:
        for raw_line in f:
            line = raw_line.strip()
            if line == "[HitObjects]":
                in_hitobjects = True
                continue
            if not in_hitobjects:
                continue
            if not line or line.startswith("//"):
                continue

            parts = line.split(",")
            if len(parts) < 5:
                continue

            x = int(parts[0])
            time_ms = int(parts[2])
            obj_type = int(parts[3])
            column = x // col_width

            # Bit 7 = mania hold note (128)
            is_ln = bool(obj_type & 128)
            end_time = 0
            if is_ln and len(parts) >= 6:
                # endTime is in the extras field: "endTime:extras"
                extras = parts[5].split(":")
                end_time = int(extras[0]) if extras[0].isdigit() else 0

            notes.append((time_ms, column, is_ln, end_time))

    notes.sort(key=lambda n: (n[0], n[1]))
    return notes


def find_osu_metadata(osu_path):
    """Đọc metadata cơ bản từ file .osu."""
    info = {}
    with open(osu_path, "r", encoding="utf-8-sig") as f:
        for raw_line in f:
            line = raw_line.strip()
            if line.startswith("CircleSize:"):
                info["key_count"] = int(line.split(":")[1].strip())
            elif line.startswith("Title:"):
                info["title"] = line.split(":", 1)[1].strip()
            elif line.startswith("Version:"):
                info["version"] = line.split(":", 1)[1].strip()
            elif line.startswith("OverallDifficulty:"):
                info["od"] = float(line.split(":")[1].strip())
            elif line == "[HitObjects]":
                break
    return info


# ===========================================================================
#  PHẦN 2: Parse replay .osr
# ===========================================================================

def extract_key_events(replay):
    """
    Trích xuất press/release events từ replay data.
    Trả về list of dict: {time_ms, action, key_index, delta, hold_ms (release only)}
    """
    events = []
    elapsed_ms = 0
    prev_state = 0
    active_holds = {}

    for frame in replay.replay_data:
        elapsed_ms += frame.time_delta
        curr_state = int(frame.keys)
        changed = prev_state ^ curr_state

        if changed:
            pressed_bits = changed & curr_state
            released_bits = changed & prev_state

            for bit_index in range(18):  # mania tối đa 18 keys
                mask = 1 << bit_index

                if pressed_bits & mask:
                    active_holds[bit_index] = elapsed_ms
                    events.append({
                        "time": elapsed_ms,
                        "action": "press",
                        "key": bit_index,
                        "delta": frame.time_delta,
                    })

                if released_bits & mask:
                    start_ms = active_holds.pop(bit_index, None)
                    hold_ms = (elapsed_ms - start_ms) if start_ms is not None else None
                    events.append({
                        "time": elapsed_ms,
                        "action": "release",
                        "key": bit_index,
                        "delta": frame.time_delta,
                        "hold_ms": hold_ms,
                    })

        prev_state = curr_state

    return events


# ===========================================================================
#  PHẦN 3: Match replay presses → beatmap notes → tính hit error
# ===========================================================================

def match_presses_to_notes(press_events, notes, key_count, window_ms=200):
    """
    Matching greedy: với mỗi note, tìm press gần nhất cùng column trong ±window_ms.
    Trả về list of (note_time, press_time, column, error_ms) và list unmatched notes.
    """
    # Group presses by column
    presses_by_col = {}
    for ev in press_events:
        col = ev["key"]
        if col < key_count:
            presses_by_col.setdefault(col, []).append(ev["time"])

    # Sort each column's presses
    for col in presses_by_col:
        presses_by_col[col].sort()

    # Track used press indices per column
    used = {col: set() for col in presses_by_col}

    matched = []
    unmatched_notes = []

    for note_time, col, is_ln, end_time in notes:
        if col not in presses_by_col:
            unmatched_notes.append((note_time, col))
            continue

        presses = presses_by_col[col]
        best_idx = None
        best_err = window_ms + 1

        # Binary search area
        import bisect
        lo = bisect.bisect_left(presses, note_time - window_ms)
        hi = bisect.bisect_right(presses, note_time + window_ms)

        for i in range(lo, hi):
            if i in used[col]:
                continue
            err = presses[i] - note_time
            if abs(err) < abs(best_err):
                best_err = err
                best_idx = i

        if best_idx is not None:
            used[col].add(best_idx)
            matched.append((note_time, presses[best_idx], col, best_err))
        else:
            unmatched_notes.append((note_time, col))

    return matched, unmatched_notes


# ===========================================================================
#  PHẦN 4: Parse console log
# ===========================================================================

def parse_console_log(log_path):
    """Parse ElanAP console log, trích xuất PERF và MANIA lines."""
    perf_data = []
    mania_data = []

    re_perf = re.compile(
        r"API - \[PERF\] (\d+)Hz \| Lat: avg=(\d+)μs min=(\d+)μs max=(\d+)μs"
    )
    re_mania = re.compile(
        r"ManiaDriver - \[MANIA\] Total: avg=(\d+)μs min=(\d+)μs max=(\d+)μs "
        r"\| SendKey: (\d+)μs \| (\d+) events"
    )

    with open(log_path, "r", encoding="utf-8") as f:
        for line in f:
            m = re_perf.search(line)
            if m:
                perf_data.append({
                    "hz": int(m.group(1)),
                    "avg": int(m.group(2)),
                    "min": int(m.group(3)),
                    "max": int(m.group(4)),
                })
                continue
            m = re_mania.search(line)
            if m:
                mania_data.append({
                    "avg": int(m.group(1)),
                    "min": int(m.group(2)),
                    "max": int(m.group(3)),
                    "sendkey": int(m.group(4)),
                    "events": int(m.group(5)),
                })

    return perf_data, mania_data


# ===========================================================================
#  PHẦN 5: Thống kê & báo cáo
# ===========================================================================

def print_section(title):
    print(f"\n{'='*60}")
    print(f"  {title}")
    print(f"{'='*60}")


def stat_line(values, unit="ms"):
    if not values:
        return "N/A"
    return (
        f"avg={statistics.mean(values):.1f}{unit}  "
        f"med={statistics.median(values):.1f}{unit}  "
        f"min={min(values)}{unit}  max={max(values)}{unit}  "
        f"stdev={statistics.stdev(values):.1f}{unit}" if len(values) > 1
        else f"avg={statistics.mean(values):.1f}{unit}  min={min(values)}{unit}  max={max(values)}{unit}"
    )


def analyze_press_intervals(events, key_count):
    """Phân tích khoảng cách giữa các lần press liên tiếp (per-key và global)."""
    per_key_times = {}
    all_press_times = []

    for ev in events:
        if ev["action"] == "press" and ev["key"] < key_count:
            per_key_times.setdefault(ev["key"], []).append(ev["time"])
            all_press_times.append(ev["time"])

    all_press_times.sort()

    # Global press intervals
    global_intervals = []
    for i in range(1, len(all_press_times)):
        gap = all_press_times[i] - all_press_times[i - 1]
        global_intervals.append(gap)

    # Per-key press intervals
    per_key_intervals = {}
    for key, times in per_key_times.items():
        times.sort()
        intervals = [times[i] - times[i - 1] for i in range(1, len(times))]
        if intervals:
            per_key_intervals[key] = intervals

    return global_intervals, per_key_intervals


def analyze_simultaneous_presses(events):
    """Phát hiện các press xảy ra cùng lúc (cùng timestamp)."""
    from collections import Counter
    press_times = [ev["time"] for ev in events if ev["action"] == "press"]
    counter = Counter(press_times)
    simultaneous = {t: c for t, c in counter.items() if c > 1}
    return simultaneous


def detect_ghost_presses(events, threshold_ms=15):
    """Phát hiện hold quá ngắn (có thể là ghost press/bounce)."""
    ghosts = []
    for ev in events:
        if ev["action"] == "release" and ev.get("hold_ms") is not None:
            if ev["hold_ms"] <= threshold_ms:
                ghosts.append(ev)
    return ghosts


def main():
    # --- Tìm files ---
    osr_files = list(SCRIPT_DIR.glob("*.osr"))
    osu_files = list(SCRIPT_DIR.glob("*.osu"))
    log_file = SCRIPT_DIR / "console.log"

    if not osr_files:
        raise FileNotFoundError("Khong tim thay file .osr trong thu muc.")

    replay_path = osr_files[0]
    print(f"Replay: {replay_path.name}")

    osu_path = osu_files[0] if osu_files else None
    if osu_path:
        print(f"Beatmap: {osu_path.name}")
    else:
        print("(Khong co file .osu — se khong tinh hit error)")

    has_log = log_file.exists()
    if has_log:
        print(f"Console log: {log_file.name}")
    else:
        print("(Khong co console.log — se khong phan tich driver perf)")

    # --- Parse replay ---
    replay = Replay.from_path(str(replay_path))
    events = extract_key_events(replay)
    presses = [e for e in events if e["action"] == "press"]
    releases = [e for e in events if e["action"] == "release"]

    # ===================================================================
    print_section("REPLAY METADATA")
    # ===================================================================
    print(f"Mode: {replay.mode}")
    print(f"Player: {replay.username}")
    print(f"Score: {replay.score}  |  Max combo: {replay.max_combo}")
    print(f"Mods: {replay.mods}")

    n_geki = replay.count_geki
    n_300 = replay.count_300
    n_katu = replay.count_katu
    n_100 = replay.count_100
    n_50 = replay.count_50
    n_miss = replay.count_miss
    total = n_geki + n_300 + n_katu + n_100 + n_50 + n_miss

    if total > 0:
        acc = (300 * (n_geki + n_300) + 200 * n_katu + 100 * n_100 + 50 * n_50) / (300 * total) * 100
    else:
        acc = 0.0

    print(f"300g:{n_geki} 300:{n_300} 200:{n_katu} 100:{n_100} 50:{n_50} miss:{n_miss}")
    print(f"Accuracy: {acc:.2f}%")
    print(f"Total frames: {len(replay.replay_data)}")
    print(f"Press events: {len(presses)}  |  Release events: {len(releases)}")

    # Determine key count from replay data
    used_keys = set(ev["key"] for ev in events)
    key_count = max(used_keys) + 1 if used_keys else 4
    key_names = [f"K{i+1}" for i in range(key_count)]
    print(f"Keys used: {key_count} ({', '.join(key_names)})")

    # ===================================================================
    print_section("PER-KEY PRESS COUNT")
    # ===================================================================
    per_key_press = {}
    per_key_hold = {}
    for ev in events:
        k = ev["key"]
        if ev["action"] == "press":
            per_key_press[k] = per_key_press.get(k, 0) + 1
        if ev["action"] == "release" and ev.get("hold_ms") is not None:
            per_key_hold.setdefault(k, []).append(ev["hold_ms"])

    for k in sorted(per_key_press.keys()):
        count = per_key_press[k]
        holds = per_key_hold.get(k, [])
        hold_str = stat_line(holds) if holds else "N/A"
        print(f"  {key_names[k] if k < len(key_names) else f'K{k+1}'}: "
              f"{count} presses  |  Hold: {hold_str}")

    # ===================================================================
    print_section("PRESS INTERVAL ANALYSIS")
    # ===================================================================
    global_intervals, per_key_intervals = analyze_press_intervals(events, key_count)

    print(f"Global (all keys): {stat_line(global_intervals)}")
    for k in sorted(per_key_intervals.keys()):
        name = key_names[k] if k < len(key_names) else f"K{k+1}"
        print(f"  {name}: {stat_line(per_key_intervals[k])}")

    # Interval distribution buckets
    if global_intervals:
        buckets = [0, 5, 10, 15, 20, 30, 50, 100, 200, 500]
        print("\n  Interval distribution (global):")
        for i in range(len(buckets)):
            lo = buckets[i]
            hi = buckets[i + 1] if i + 1 < len(buckets) else float("inf")
            count = sum(1 for v in global_intervals if lo <= v < hi)
            if count > 0:
                label = f"{lo}-{hi}ms" if hi != float("inf") else f"{lo}ms+"
                bar = "#" * min(count, 60)
                print(f"    {label:>10}: {count:>4}  {bar}")

    # ===================================================================
    print_section("GHOST PRESS DETECTION (hold <= 15ms)")
    # ===================================================================
    ghosts = detect_ghost_presses(events, threshold_ms=15)
    if ghosts:
        print(f"Found {len(ghosts)} ghost presses (possible key bounce):")
        for g in ghosts[:20]:
            name = key_names[g["key"]] if g["key"] < len(key_names) else f"K{g['key']+1}"
            print(f"  t={g['time']}ms  {name}  hold={g['hold_ms']}ms")
        if len(ghosts) > 20:
            print(f"  ... và {len(ghosts) - 20} ghost presses khác")
    else:
        print("Không phát hiện ghost press.")

    # ===================================================================
    print_section("SIMULTANEOUS PRESSES")
    # ===================================================================
    simul = analyze_simultaneous_presses(events)
    if simul:
        print(f"Found {len(simul)} timestamps with multiple presses:")
        sorted_simul = sorted(simul.items())[:30]
        for t, count in sorted_simul:
            keys_at_t = [ev["key"] for ev in events
                         if ev["action"] == "press" and ev["time"] == t]
            names = [key_names[k] if k < len(key_names) else f"K{k+1}" for k in keys_at_t]
            print(f"  t={t}ms: {count} keys ({', '.join(names)})")
    else:
        print("Không có press đồng thời (mỗi press ở frame riêng).")

    # ===================================================================
    #  PHẦN 6: HIT ERROR (cần file .osu)
    # ===================================================================
    if osu_path:
        meta = find_osu_metadata(str(osu_path))
        osu_key_count = meta.get("key_count", key_count)
        od = meta.get("od", 8.0)
        title = meta.get("title", "?")
        version = meta.get("version", "?")

        # Mania hit windows (ms) based on OD
        # https://osu.ppy.sh/wiki/en/Beatmap/Overall_difficulty
        w_max = 16  # MAX (rainbow 300)
        w_300 = 64 - 3 * od
        w_200 = 97 - 3 * od
        w_100 = 127 - 3 * od
        w_50 = 151 - 3 * od
        w_miss_window = 188 - 3 * od

        print_section(f"HIT ERROR ANALYSIS — {title} [{version}]")
        print(f"Keys: {osu_key_count}K  |  OD: {od}")
        print(f"Hit windows: MAX={w_max:.0f}ms  300={w_300:.0f}ms  "
              f"200={w_200:.0f}ms  100={w_100:.0f}ms  50={w_50:.0f}ms")

        notes = parse_osu_mania_notes(str(osu_path), osu_key_count)
        print(f"Total notes in beatmap: {len(notes)}")

        matched, unmatched = match_presses_to_notes(presses, notes, osu_key_count)
        print(f"Matched: {len(matched)}  |  Unmatched notes: {len(unmatched)}")

        if matched:
            all_errors = [err for _, _, _, err in matched]
            per_col_errors = {}
            for _, _, col, err in matched:
                per_col_errors.setdefault(col, []).append(err)

            print(f"\nOverall hit error:  {stat_line(all_errors)}")
            # Positive = late, negative = early
            early = sum(1 for e in all_errors if e < 0)
            late = sum(1 for e in all_errors if e > 0)
            perfect = sum(1 for e in all_errors if e == 0)
            print(f"  Early: {early}  |  Perfect: {perfect}  |  Late: {late}")

            # Mean error per key (> 0 = consistently late = driver adds delay)
            print(f"\nPer-key hit error (negative=early, positive=late):")
            for col in sorted(per_col_errors.keys()):
                errors = per_col_errors[col]
                name = key_names[col] if col < len(key_names) else f"K{col+1}"
                mean_err = statistics.mean(errors)
                direction = "LATE" if mean_err > 2 else "EARLY" if mean_err < -2 else "OK"
                print(f"  {name}: {stat_line(errors)}  [{direction}]")

            # Hit error distribution
            print(f"\nHit error distribution:")
            err_buckets = [
                (-200, -w_50, "MISS(early)"),
                (-w_50, -w_100, "50(early)"),
                (-w_100, -w_200, "100(early)"),
                (-w_200, -w_300, "200(early)"),
                (-w_300, -w_max, "300(early)"),
                (-w_max, w_max, "MAX"),
                (w_max, w_300, "300(late)"),
                (w_300, w_200, "200(late)"),
                (w_200, w_100, "100(late)"),
                (w_100, w_50, "50(late)"),
                (w_50, 200, "MISS(late)"),
            ]
            for lo, hi, label in err_buckets:
                count = sum(1 for e in all_errors if lo <= e < hi)
                pct = count / len(all_errors) * 100
                bar = "#" * int(pct)
                print(f"    {label:>12}: {count:>4} ({pct:5.1f}%)  {bar}")

            # Top 20 worst errors
            print(f"\nTop 20 worst hit errors:")
            worst = sorted(matched, key=lambda m: abs(m[3]), reverse=True)[:20]
            for note_t, press_t, col, err in worst:
                name = key_names[col] if col < len(key_names) else f"K{col+1}"
                direction = "late" if err > 0 else "early"
                print(f"  note={note_t}ms  press={press_t}ms  {name}  "
                      f"error={err:+.0f}ms ({direction})")

    # ===================================================================
    #  PHẦN 7: Console log analysis
    # ===================================================================
    if has_log:
        perf_data, mania_data = parse_console_log(str(log_file))

        if perf_data:
            print_section("DRIVER PERF — API (from console.log)")
            hz_vals = [d["hz"] for d in perf_data]
            avg_vals = [d["avg"] for d in perf_data]
            max_vals = [d["max"] for d in perf_data]
            print(f"Samples: {len(perf_data)}")
            print(f"Hz:      {stat_line(hz_vals, 'Hz')}")
            print(f"Avg lat: {stat_line(avg_vals, 'μs')}")
            print(f"Max lat: {stat_line(max_vals, 'μs')}")

            # Hz stability
            low_hz = sum(1 for h in hz_vals if h < 125)
            print(f"\nHz < 125: {low_hz}/{len(hz_vals)} samples "
                  f"({low_hz/len(hz_vals)*100:.0f}%)")

            # Max latency spikes > 3ms
            spikes = [d for d in perf_data if d["max"] > 3000]
            print(f"Spikes > 3ms: {len(spikes)}/{len(perf_data)} samples")

        if mania_data:
            print_section("DRIVER PERF — ManiaDriver (from console.log)")
            avg_vals = [d["avg"] for d in mania_data]
            max_vals = [d["max"] for d in mania_data]
            sk_vals = [d["sendkey"] for d in mania_data]
            print(f"Samples: {len(mania_data)}")
            print(f"Total avg: {stat_line(avg_vals, 'μs')}")
            print(f"Total max: {stat_line(max_vals, 'μs')}")
            print(f"SendKey:   {stat_line(sk_vals, 'μs')}")

    # ===================================================================
    print_section("CALIBRATION SUMMARY")
    # ===================================================================
    print("Dán toàn bộ output này cho AI để hiệu chuẩn driver.")
    print("Nếu có file .osu: hit error per-key cho thấy zone nào bị delay.")
    print("Nếu có console.log: latency spikes cho thấy driver bottleneck.")
    print("Ghost presses → cần debounce. Short intervals → cần polling rate cao hơn.")


if __name__ == "__main__":
    main()
