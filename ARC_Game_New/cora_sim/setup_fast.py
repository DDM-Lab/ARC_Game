"""Opt-in Cython build of the surrogate's two hot modules.

    cd cora_sim && python setup_fast.py build_ext --inplace     # build
    rm cora_sim/*.so                                            # go back to pure Python

MEASURED (macOS arm64, 32-round episodes, warmed):

    pure Python, serial          116 episodes/s     269 us/round
    cythonised, serial           155 episodes/s     204 us/round   (1.3x)
    pure Python + 10 processes   515 episodes/s
    cythonised + 10 processes    643 episodes/s                    (5.5x over baseline)

`rng.py` and `flood.py` are compiled AS THEY ARE -- no type annotations, no rewrite, no
separate .pyx. Cython accepts plain Python, so the source stays readable and stays the
single definition of the mechanics; the build is a pure speed switch that can be deleted at
any time. Verified after each build: identical trajectories to pure Python across 6 episodes
(budget, satisfaction, draw count, tile count, every counter) and test_flood still 172/172
against Unity's own numbers.

TWO THINGS TO KNOW

1. A built .so SHADOWS the .py next to it. While you are editing flood.py or rng.py, delete
   the .so first or your edits will be silently ignored -- the import system prefers the
   extension module. That is why this is opt-in and the artifacts are gitignored.

2. The .so is platform-specific (macOS arm64 here). The cluster must run this build itself,
   with a compiler available. Put it in the job script, not on a submit node by hand.

SCALING NOTE. Rollouts are independent, so a process pool is near-linear on homogeneous
cores; the 4.1x measured for 10 processes here is held back by this laptop's efficiency
cores. At 128 cores the per-worker startup (flood map, corpus, parameter sheet) stops being
noise -- give each worker a large chunksize, or a persistent worker that loops over many
episodes, so the setup amortises.

WHY NOT numpy/JAX. The draw stream is the surrogate's whole value and it cannot be
vectorised: Unity uses xorshift128, whose nth value needs n sequential state updates (unlike
JAX's counter-based Threefry, where any draw is a pure function of (key, index)), and our
draw count per round is data-dependent -- measured mean 144, min 0, max 656, so ~50% of
masked lane-work would be wasted even before the desync problem. Stubbing the flood out
entirely only buys 42% of a round, and ~43us of that is the draws themselves, so the ceiling
for any flood rewrite is about 1.3x -- the same as this build, for none of the risk.
"""
from setuptools import setup
from Cython.Build import cythonize

setup(
    name="cora_sim_fast",
    ext_modules=cythonize(
        ["rng.py", "flood.py"],
        language_level=3,
        compiler_directives={"boundscheck": False, "wraparound": False},
    ),
)
