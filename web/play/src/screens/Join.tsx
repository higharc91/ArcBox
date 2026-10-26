import { useState } from "preact/hooks";
import { useFeedback } from "../feedback/feedback";

export const NAME_MAX = 12;
const CODE_LENGTH = 4;

export const cleanCode = (raw: string) => raw.toUpperCase().replace(/[^A-Z]/g, "").slice(0, CODE_LENGTH);

export function isValidName(name: string): boolean {
  const n = name.trim();
  return n.length >= 1 && n.length <= NAME_MAX;
}

interface Props {
  code?: string;
  name?: string;
  error?: string;
  busy?: boolean;
  onJoin: (code: string, name: string) => void;
}

export function Join({ code: initialCode = "", name: initialName = "", error, busy, onJoin }: Props) {
  const [code, setCode] = useState(cleanCode(initialCode));
  const [name, setName] = useState(initialName);
  const valid = code.length === CODE_LENGTH && isValidName(name);
  const feedback = useFeedback();

  const submit = (e: Event) => {
    e.preventDefault();
    feedback?.unlock(); // the Join tap is the user gesture iOS needs before any sound
    if (valid && !busy) onJoin(code, name.trim());
  };

  return (
    <div class="screen join">
      <div class="brand-bar"><span class="brand">ARCBOX</span></div>
      <form class="join-form" onSubmit={submit}>
        <h1>Join a game</h1>
        <label class="field">
          <span>Room code</span>
          <input
            class="text-input code-input"
            name="code"
            value={code}
            inputMode="text"
            autoCapitalize="characters"
            autoComplete="off"
            spellcheck={false}
            maxLength={CODE_LENGTH}
            placeholder="ABCD"
            onInput={(e) => {
              const el = e.currentTarget as HTMLInputElement;
              const clean = cleanCode(el.value);
              el.value = clean; // keep the box in step even when the state doesn't change
              setCode(clean);
            }}
          />
        </label>
        <label class="field">
          <span>Your name</span>
          <input
            class="text-input"
            name="name"
            value={name}
            autoComplete="nickname"
            maxLength={NAME_MAX}
            placeholder={`Up to ${NAME_MAX} characters`}
            onInput={(e) => setName((e.currentTarget as HTMLInputElement).value)}
          />
        </label>
        <button type="submit" class="primary big" disabled={!valid || busy}>
          {busy ? "Joining…" : "Join"}
        </button>
        {error ? <p class="form-error" role="alert">{error}</p> : null}
      </form>
    </div>
  );
}
