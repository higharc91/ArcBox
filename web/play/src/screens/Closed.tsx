export function Closed({ reason, onLeave }: { reason: string; onLeave: () => void }) {
  return (
    <div class="screen closed">
      <div class="closed-box">
        <small>ROOM ENDED</small>
        <p class="closed-reason">{reason}</p>
        <p class="hint">Ask the host to start a new room, then join it.</p>
      </div>
      <div class="actions">
        <button type="button" class="primary big" onClick={onLeave}>Join another room</button>
      </div>
    </div>
  );
}
