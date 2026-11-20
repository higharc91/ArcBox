import type { LobbyTheme } from "../module";
import { useLogo } from "./art";

/**
 * Meridian's lobby theme (GameModule.lobbyTheme; #335, M5 plan, Task 5): while a room has Meridian selected, the shell's
 * lobby and "Who are you?" screens sit on the ship's dark hull with the logo on top, or the game's name until the logo
 * art lands.
 */
function Banner() {
  const logo = useLogo();
  return (
    <div class="mer-lobby-banner" aria-hidden="true">
      {logo ? <img src={logo} alt="" /> : <span>DERELICT: ISV MERIDIAN</span>}
    </div>
  );
}

function Backdrop() {
  return <div class="mer-lobby-backdrop" aria-hidden="true" />;
}

export const lobbyTheme: LobbyTheme = {
  className: "mer-lobby",
  Backdrop,
  Banner,
  text: {},
};
