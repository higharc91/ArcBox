import { render } from "preact";
import "./src/fonts";
import "./src/theme.css";
import "./src/games/tactics/tactics.css";
import { MoodPortrait } from "./src/games/tactics/art";
import cast from "../../content/tactics/base/lore/characters.json";
const page = Number(new URLSearchParams(location.search).get("page") ?? 0);
const moods = ["idle", "happy", "sad", "angry", "surprised", "hurt"];
render(<main class="tx" style={{ padding: "12px", maxWidth: "390px", margin: "auto" }}><h1>Story portraits · {page + 1}/4</h1>
 {cast.characters.slice(page * 5, page * 5 + 5).map(c => <section><h2>{c.name}</h2><div style={{ display: "grid", gridTemplateColumns: "repeat(3, 80px)", gap: "12px", justifyContent: "space-between" }}>
  {moods.map(mood => <div style={{ width: "80px", textAlign: "center" }}><MoodPortrait preset={"portrait" in c ? c.portrait : c.preset} mood={mood} name={c.name} size={80} /><small>{mood}</small></div>)}
 </div></section>)}
</main>, document.getElementById("app")!);
