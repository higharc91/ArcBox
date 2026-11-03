# Steam refinement

The user requested slower steam dissipation after reviewing milestone E's animation reel. Runtime/tests: **`4fe07788`**.

Thawing and dousing clouds now last **2.4 seconds**, rather than roughly 0.6 seconds. Each cloud plays through its existing
manifest frames at the slower rate, rises **12 art pixels**, expands **18%**, and gently fades with a smooth curve. It stays
on its tile as the rest of the fight continues. Action completion retains its original timing; the steam tail does not hold up
turns or replay an acknowledgement. Other reaction effects keep their existing animation settings.

This user-requested visual tail supersedes E's original under-one-second visual duration for steam. The action's own hold
remains below one second.

[Updated steam preview](steam-preview.mp4)

Two regression cases (thaw and douse) first failed because the old cloud had already disappeared. They now verify that the
beat completes once, steam remains visible afterwards, its frames advance slowly, it rises and expands while fading, and
it clears completely. The existing large-front script check allows the longer steam tail while retaining the short action hold.

Final test results, source fingerprints and motion checks are recorded in [steam-validation.json](steam-validation.json).

Validation: **5,853 .NET tests**, **478 web tests**, and **194 focused presentation tests** pass; **0 build warnings**.
The six golden replay/event files are unchanged. [Solution log](steam-tests.txt) · [Web log](steam-web.txt) ·
[Host build](steam-host-build.txt) · [TV capture](steam-tv.txt). The capture has the same software-driver/shutdown warnings documented in E.
