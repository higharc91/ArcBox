import { render } from "preact";
import { useState } from "preact/hooks";
import "./src/fonts";
import "./src/theme.css";
import { module } from "./src/games/tactics/index";
import inn from "./test/fixtures/tactics/inn-rooms.json";
import temple from "./test/fixtures/tactics/temple.json";
import camp from "./test/fixtures/tactics/camp-job.json";
const fixtures = { inn, temple, camp };
const which = (new URLSearchParams(location.search).get("fixture") ?? "inn") as keyof typeof fixtures;
function Evidence() {
  const initial = fixtures[which] ?? inn;
  const [data, setData] = useState<Record<string, unknown>>(initial.data);
  const Screen = module.screens[initial.screen]!;
  const send = (action: string) => { if (action === "backgroundSecretRead") { const { backgroundSecret, ...remaining } = data; setData(remaining); } };
  return <main class="screen" data-game="tactics"><Screen data={data} send={send} stream={send} viewId="evidence" /></main>;
}
render(<Evidence />, document.getElementById("app")!);
