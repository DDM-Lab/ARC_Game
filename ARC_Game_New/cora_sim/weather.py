"""Port of WeatherSystem.GenerateRandomWeather -- one draw, once per day change.

Small mechanic, outsized importance: the weather it picks sets flood's expansion rate,
spread multiplier and shrinkage for every round of the following day, so getting it wrong
does not cost one draw, it costs the whole day's flood trajectory.

TWO THINGS THAT ARE NOT WHAT THEY LOOK LIKE:

1. Random.Range(0f, total) is REVERSED (see rng.range_float). The forward form agrees with
   Unity near the middle of the range and disagrees at the ends, so a short fixture can
   look fine while the tails are inverted -- Sunny and Storm swap places.

2. The probabilities are SCENE values, and the array ORDER is what the cumulative walk
   consumes. WeatherSystem.Awake forces weatherTypes[i].weatherType = (WeatherType)i, so
   the order is the enum order, but the probabilities are not the ones in the .cs file.
   Both come from the sim_constants export.
"""
from __future__ import annotations

import json as _json
import os as _os

from .rng import f32add

_CONST_PATH = _os.path.join(_os.path.dirname(_os.path.abspath(__file__)),
                            "corpus", "sim_constants.json")

# WeatherSystem.GetRainIntensity(). Not exported by sim_constants because it is a switch in
# code rather than serialized data -- if that ever changes, export it and delete this.
RAIN_INTENSITY = {"Sunny": 0.0, "SmallRain": 0.3, "MediumRain": 0.6,
                  "HeavyRain": 0.8, "Storm": 1.0}


def load_weather_table(path=None):
    """[(weather, probability)] in the array order the cumulative walk uses."""
    d = _json.load(open(path or _CONST_PATH))
    rows = d.get("weather")
    if not rows:
        raise KeyError("sim_constants.json has no 'weather' block -- re-export it from a "
                       "build that includes the weather export, do not hand-write it")
    return [(r["weather"], float(r["probability"])) for r in sorted(rows, key=lambda r: r["index"])]


TABLE = load_weather_table()


def generate_weather(rng, table=None, marks=None):
    """One Weather.select draw. Returns the chosen weather name.

    Mirrors the fallthrough too: if the drawn value exceeds the cumulative total, Unity
    falls back to Sunny rather than clamping to the last entry."""
    table = TABLE if table is None else table
    total = f32add(*[p for _, p in table]) if table else 0.0
    if marks is not None:
        marks.append("draw:Weather.select")
    value = rng.range_float(0.0, total)
    cumulative = 0.0
    for name, p in table:
        cumulative = f32add(cumulative, p)
        if value <= cumulative:
            return name
    return "Sunny"
