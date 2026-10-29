import type { CustomView } from "../protocol";
import { useGameModule } from "../games/registry";
import { Wait } from "./Wait";
import type { Send } from "./common";

/**
 * A screen a game module defines (CustomView). Renders `module.screens[view.screen]` with the view's data and a
 * `send(action, data)` that posts a CustomInput. Unlike the generic widgets it never locks after a send: a d-pad or a
 * slider sends again and again on the same viewId, and the game decides what each action means.
 * While the module is still downloading this is the generic wait with a spinner; when the module is missing, or lacks the screen
 * (an older app than the server), a card says so.
 */
export function Custom({ view, send }: { view: CustomView; send: Send }) {
  const { module, state } = useGameModule(view.module);
  const Screen = module?.screens?.[view.screen];
  if (Screen) {
    return <Screen data={view.data} send={(action, data) => send(data === undefined ? { action } : { action, data })} />;
  }
  if (state === "loading") {
    return (
      <div class="custom-loading">
        <div class="spinner" aria-hidden="true" />
        <Wait view={{ kind: "wait", viewId: view.viewId, message: "Loading…" }} />
      </div>
    );
  }
  return <NewerApp />;
}

function NewerApp() {
  return (
    <div class="w-wait newer-app">
      <p class="wait-message">This screen needs a newer app</p>
      <button type="button" onClick={() => location.reload()}>Reload</button>
    </div>
  );
}
