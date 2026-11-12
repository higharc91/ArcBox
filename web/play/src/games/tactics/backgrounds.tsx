import type { ComponentType } from "preact";
import type { CustomScreenProps } from "../module";
import { Sheet } from "./details";

interface Secret { id: string; background: string; text: string }
/** Secrets arrive only in the authenticated owner's packet; dismissal is persisted server-side. */
export function withBackgroundSecret(Screen: ComponentType<CustomScreenProps>): ComponentType<CustomScreenProps> {
  return function PrivateBackgroundScreen(props) {
    const { backgroundSecret: secret, map } = props.data as { backgroundSecret?: Secret; map?: unknown };
    const remember = () => props.send("backgroundSecretRead", {});
    return <><Screen {...props} />{secret && !map && <Sheet title={`${secret.background} · Yours alone`} eyebrow="Your background" onClose={remember}
      foot={<button class="tx-btn gold" onClick={remember}>Remember this</button>}>
      <p class="tx-note">This secret is private to your phone.</p><p class="tx-background-secret">{secret.text}</p>
    </Sheet>}</>;
  };
}
