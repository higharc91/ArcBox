import { useEffect, useMemo, useState } from "preact/hooks";
import type { KeyValueStore } from "../session/storage";
import type { Transport } from "../session/transport";
import { HostClient } from "./host";
import { HostPanel } from "./HostPanel";

export const MAX_PHONES = 8;

/** `?phones=N`, default 4, clamped to 1..8. */
export function phoneCount(search: string): number {
  const n = parseInt(new URLSearchParams(search).get("phones") ?? "", 10);
  return Number.isNaN(n) ? 4 : Math.min(MAX_PHONES, Math.max(1, n));
}

export const phoneUrl = (code: string, slot: number) => `/?room=${code}&slot=${slot}&devName=P${slot}`;

function Phone({ code, slot }: { code: string; slot: number }) {
  const [generation, setGeneration] = useState(0);
  return (
    <div class="dev-phone">
      <div class="dev-phone-bar">
        <span>P{slot}</span>
        <button type="button" onClick={() => setGeneration((g) => g + 1)} aria-label={`Reload phone ${slot}`}>reload</button>
      </div>
      <iframe key={generation} src={phoneUrl(code, slot)} title={`Phone ${slot}`} />
    </div>
  );
}

interface Props {
  transport: Transport;
  search: string;
  /** Holds host tokens as `arcbox-host:<CODE>` (sessionStorage in the browser). */
  store: KeyValueStore;
  /** Called with the room code once hosting, e.g. to put `?room=` in the address bar so a reload resumes. */
  onRoom?: (code: string) => void;
}

/** `/dev.html?phones=N[&room=CODE]`: hosts a room in the browser and shows N phones side by side. */
export function DevPage({ transport, search, store, onRoom }: Props) {
  const host = useMemo(() => new HostClient(transport, store), [transport, store]);
  const [code, setCode] = useState<string | null>(null);
  const phones = phoneCount(search);

  useEffect(() => {
    const room = new URLSearchParams(search).get("room")?.trim().toUpperCase() || null;
    // Follow the host's room code: the first connect, and a fresh room if the server lost the old one.
    let current: string | null = null;
    const follow = (c: string | null) => {
      if (!c || c === current) return;
      current = c;
      setCode(c);
      onRoom?.(c);
    };
    const unsubscribe = host.subscribe((s) => follow(s.code));
    void host.connect(room).then(follow);
    const visible = () => document.visibilityState === "visible" && void host.onVisible();
    document.addEventListener("visibilitychange", visible);
    return () => {
      unsubscribe();
      document.removeEventListener("visibilitychange", visible);
    };
  }, [host, search]);

  return (
    <div class="dev">
      <HostPanel host={host} />
      <section class="dev-phones">
        {code ? Array.from({ length: phones }, (_, i) => <Phone key={`${code}-${i + 1}`} code={code} slot={i + 1} />) : <p class="host-idle">Creating a room…</p>}
      </section>
    </div>
  );
}
