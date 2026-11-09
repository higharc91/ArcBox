// Dev-only fixture preview; not included in production entry points.
import { render } from "preact";
import "../fonts";
import "../theme.css";
import { loadModule } from "../games/registry";
import { ControllerScreen } from "../widgets/ControllerScreen";
import type { CustomView, CampaignResponse } from "../protocol";
import { TacticsPortal } from "../games/tactics/portal";
const fixtures = import.meta.glob<{ default: CustomView }>("../../test/fixtures/tactics/*.json", { eager: true });
const name = new URLSearchParams(location.search).get("screen") ?? "questlog-leader";
const view = structuredClone(fixtures[`../../test/fixtures/tactics/${name}.json`]!.default);
await loadModule("tactics");
if (name.startsWith("portal-")) {
  const campaign = { info: { id: "qa", code: "TEST", gameId: "tactics", name: "Preview party", owner: "pr_ana", members: [], version: 1 }, view, leasedBy: null } as unknown as CampaignResponse;
  render(<div class="screen portal"><TacticsPortal campaign={campaign} readOnly={false} send={async () => {}} /></div>, document.getElementById("app")!);
} else render(<ControllerScreen view={view} send={payload => console.info("PHONE", payload)} me="ANA" game="tactics" />, document.getElementById("app")!);
