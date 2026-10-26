import { render } from "preact";
import "./fonts";
import "./theme.css";
import { App, boot } from "./App";
import { Feedback } from "./feedback/feedback";
import { Session } from "./session/session";
import { browserStore } from "./session/storage";
import { signalrTransport } from "./session/transport";
import { installWakeLockRefresh } from "./widgets/wakeLock";

const params = new URLSearchParams(location.search);
const store = browserStore();
const session = new Session(signalrTransport("/hub"), store, params.get("slot") ?? undefined);

// A phone that slept or switched apps may come back with a dead socket: reconnect and rejoin.
document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "visible") void session.onVisible();
});
window.addEventListener("pageshow", () => void session.onVisible());
installWakeLockRefresh();

// Haptics and sounds. Audio unlocks on the Join tap, or on any tap after a rejoin that skipped the Join form.
const feedback = new Feedback(store);
feedback.installGestureUnlock(document);

render(<App session={session} feedback={feedback} />, document.getElementById("app")!);
void boot(session, store, location.search);
