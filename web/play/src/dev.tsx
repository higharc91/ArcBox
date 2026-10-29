import { render } from "preact";
import "./fonts";
import "./theme.css";
import "./dev/dev.css";
import { DevPage } from "./dev/DevPage";
import { signalrTransport } from "./session/transport";
import { browserStore } from "./session/storage";

function hostStore() {
  try {
    sessionStorage.setItem("arcbox:probe", "1");
    sessionStorage.removeItem("arcbox:probe");
    return sessionStorage;
  } catch {
    return browserStore();
  }
}

// Keep ?room= in the address bar so reloading the dev page resumes hosting the same room.
const onRoom = (code: string) => {
  const url = new URL(location.href);
  if (url.searchParams.get("room") === code) return;
  url.searchParams.set("room", code);
  history.replaceState(null, "", url);
};

render(
  <DevPage transport={signalrTransport("/hub")} search={location.search} store={hostStore()} onRoom={onRoom} />,
  document.getElementById("app")!,
);
