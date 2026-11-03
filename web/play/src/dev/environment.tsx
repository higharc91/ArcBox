// Dev-only rendering fixture: production builds do not include environment.html.
import { render } from "preact";
import "../fonts";
import "../theme.css";
import { loadModule } from "../games/registry";
import { ControllerScreen } from "../widgets/ControllerScreen";
import type { CustomView } from "../protocol";
import turn from "../../test/fixtures/tactics/environment-turn.json";
import status from "../../test/fixtures/tactics/environment-status.json";

const params = new URLSearchParams(location.search);
const view = structuredClone(params.get("screen") === "status" ? status : turn) as CustomView;
view.deadlineUtc = new Date(Date.now() + 60_000).toISOString();
// Optional overrides let the same real controller check each look, safe clearing, and reduced motion.
if (params.has("surface")) Object.assign(view.data as object, { surfaces: params.get("surface")!.split(",").filter(Boolean) });
await loadModule("tactics");
render(<ControllerScreen view={view} send={payload => console.info("PHONE", payload)} me="PLAYER" game="tactics" />,
  document.getElementById("app")!);
