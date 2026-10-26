import type { WaitView } from "../protocol";

export function Wait({ view }: { view: WaitView }) {
  return (
    <div class="w-wait">
      <p class="wait-message">{view.message}</p>
    </div>
  );
}
