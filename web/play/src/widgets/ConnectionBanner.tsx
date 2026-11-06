import { useEffect, useState } from "preact/hooks";
import type { ConnectionStatus } from "../session/session";
import "./connection.css";

export function ConnectionBanner({ connection, retry }: { connection?: ConnectionStatus; retry: () => void }) {
  const since = connection && connection !== "recovered" ? connection.since : undefined;
  const [slow, setSlow] = useState(false);
  useEffect(() => {
    setSlow(false);
    if (since === undefined) return;
    const timer = setTimeout(() => setSlow(true), Math.max(0, since + 15000 - Date.now()));
    return () => clearTimeout(timer);
  }, [since]);
  if (!connection) return null;
  if (connection === "recovered") return <div class="connection-banner" role="status">Back in the game</div>;
  return <div class="connection-banner" role="status">
    <span>Reconnecting… if this takes long, the AI will play your hero.</span>
    {slow && <div>Check your Wi-Fi and that the room is still open. Retry {connection.attempts}.
      <button type="button" onClick={retry}>Try again</button>
    </div>}
  </div>;
}
