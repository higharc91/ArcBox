import { render } from "preact";
import "../games/tactics/fonts";
import "./title.css";
import { TitleScreen } from "./TitleScreen";

/**
 * `title.html`: Wardenflame's start screen for the TV, a flyover tour of the Vale under the logo. It stands alone (no
 * hub connection). Query parameters:
 *   room=ABCD   show "Join at <host> with code ABCD"
 *   host=...    the address to show instead of this page's host (when the TV opened it as localhost)
 *   t=<s>       freeze the tour at that moment (screenshots); gleam=<frame> freezes the logo gleam
 * With prefers-reduced-motion the tour is a still frame of the first area.
 */
const params = new URLSearchParams(location.search);
const num = (key: string) => {
  const v = params.get(key);
  return v !== null && v !== "" && !Number.isNaN(Number(v)) ? Number(v) : undefined;
};

render(
  <TitleScreen
    room={params.get("room")?.trim().toUpperCase() || null}
    joinHost={params.get("host") ?? undefined}
    frozenAt={num("t")}
    gleamFrame={num("gleam")}
    reducedMotion={window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false}
  />,
  document.getElementById("app")!,
);
