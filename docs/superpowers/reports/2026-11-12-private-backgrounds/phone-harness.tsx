import { render } from "preact";
import { useState } from "preact/hooks";
import "./src/fonts";
import "./src/theme.css";
import { module } from "./src/games/tactics/index";
import secret from "./test/fixtures/tactics/walk-background-secret.json";
import owner from "./test/fixtures/tactics/vote-background-owner.json";
import leader from "./test/fixtures/tactics/vote-background-leader.json";
const fixtures = { secret, owner, leader };
const which = (new URLSearchParams(location.search).get("fixture") ?? "secret") as keyof typeof fixtures;
function Evidence() {
  const initial = fixtures[which] ?? secret;
  const [data, setData] = useState<Record<string, unknown>>(initial.data);
  const Screen = module.screens[initial.screen]!;
  const send = (action: string) => { if (action === "backgroundSecretRead") { const { backgroundSecret, ...remaining } = data; setData(remaining); } };
  return <main class="screen" data-game="tactics"><Screen data={data} send={send} stream={send} viewId="evidence" /></main>;
}
render(<Evidence />, document.getElementById("app")!);
