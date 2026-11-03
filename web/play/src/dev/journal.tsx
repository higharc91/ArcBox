// Dev-only fixture preview; not included in production entry points.
import { render } from "preact";
import "../fonts";
import "../theme.css";
import { loadModule } from "../games/registry";
import { ControllerScreen } from "../widgets/ControllerScreen";
import type { CustomView } from "../protocol";
const fixtures = import.meta.glob<{ default: CustomView }>("../../test/fixtures/tactics/*.json", { eager: true });
const name = new URLSearchParams(location.search).get("screen") ?? "questlog-leader";
const view = structuredClone(fixtures[`../../test/fixtures/tactics/${name}.json`]!.default);
await loadModule("tactics");
render(<ControllerScreen view={view} send={payload => console.info("PHONE", payload)} me="ANA" game="tactics" />, document.getElementById("app")!);
