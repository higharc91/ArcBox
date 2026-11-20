var CrewAudio = (() => {
  var __defProp = Object.defineProperty;
  var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
  var __getOwnPropNames = Object.getOwnPropertyNames;
  var __hasOwnProp = Object.prototype.hasOwnProperty;
  var __export = (target, all) => {
    for (var name in all)
      __defProp(target, name, { get: all[name], enumerable: true });
  };
  var __copyProps = (to, from, except, desc) => {
    if (from && typeof from === "object" || typeof from === "function") {
      for (let key of __getOwnPropNames(from))
        if (!__hasOwnProp.call(to, key) && key !== except)
          __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
    }
    return to;
  };
  var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

  // <stdin>
  var stdin_exports = {};
  __export(stdin_exports, {
    CREW_DURATION: () => CREW_DURATION,
    CREW_GAIN: () => CREW_GAIN,
    CREW_SIGNATURES: () => CREW_SIGNATURES,
    CrewSelection: () => CrewSelection,
    MAX_GAIN: () => MAX_GAIN,
    Sounds: () => Sounds,
    crewSound: () => crewSound,
    playCrewSignature: () => playCrewSignature
  });

  // web/play/src/feedback/crew-tones.ts
  var CREW_SIGNATURES = {
    ines_okafor: { name: "Ines Okafor", character: "a warm, reassuring toolbox chime", wave: "triangle", notes: [392, 494, 587] },
    tobe_raske: { name: "Tobe Raske", character: "a round little two-part rigging plonk", wave: "sine", notes: [262, 392, 330] },
    sable_quist: { name: "Sable Quist", character: "a light upward pilot whistle", wave: "sine", notes: [523, 659, 880] },
    anselm_varga: { name: "Dr. Anselm Varga", character: "a gentle, rounded medical scanner reply", wave: "sine", notes: [440, 554, 440] },
    pell_ostrander: { name: "Pell Ostrander", character: "a playful three-note terminal greeting", wave: "triangle", notes: [659, 523, 784] },
    juno_achterberg: { name: "Juno Achterberg", character: "a sturdy little cargo-latch boop", wave: "triangle", notes: [294, 440, 587] },
    kit_sato: { name: "Kit Sato", character: "a curious quick scout chirrup", wave: "sine", notes: [740, 988, 831] },
    rhee_callender: { name: "Rhee Callender", character: "a soft, sparkling radio hello", wave: "triangle", notes: [587, 784, 988] }
  };
  var CREW_GAIN = 0.12;
  var CREW_DURATION = 0.42;
  function crewSound(id) {
    return typeof id === "string" && Object.hasOwn(CREW_SIGNATURES, id) ? `crew.${id}` : void 0;
  }
  function playCrewSignature(ctx, id, at) {
    const signature = CREW_SIGNATURES[id];
    signature.notes.forEach((frequency, i) => {
      const start = at + i * 0.075;
      const length = i === 2 ? 0.27 : 0.18;
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = signature.wave;
      osc.frequency.setValueAtTime(frequency, start);
      osc.frequency.exponentialRampToValueAtTime(frequency * (i === 2 ? 1.035 : 1), start + length);
      gain.gain.setValueAtTime(1e-4, start);
      gain.gain.exponentialRampToValueAtTime(CREW_GAIN, start + 9e-3);
      gain.gain.exponentialRampToValueAtTime(CREW_GAIN * 0.35, start + 0.08);
      gain.gain.exponentialRampToValueAtTime(1e-4, start + length);
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.onended = () => {
        osc.disconnect();
        gain.disconnect();
      };
      osc.start(start);
      osc.stop(start + length);
    });
  }

  // web/play/src/feedback/sounds.ts
  var MAX_GAIN = 0.25;
  var FLOOR = 1e-4;
  function contextClass() {
    const g = globalThis;
    return g.AudioContext ?? g.webkitAudioContext;
  }
  function envelope(ctx, at, peak, length, attack = 4e-3) {
    const g = ctx.createGain();
    g.gain.setValueAtTime(FLOOR, at);
    g.gain.exponentialRampToValueAtTime(Math.min(peak, MAX_GAIN), at + attack);
    g.gain.exponentialRampToValueAtTime(FLOOR, at + length);
    g.connect(ctx.destination);
    return g;
  }
  function tone(ctx, at, t, into) {
    const start = at + (t.delay ?? 0);
    const osc = ctx.createOscillator();
    osc.type = t.type;
    osc.frequency.setValueAtTime(t.from, start);
    if (t.to) osc.frequency.exponentialRampToValueAtTime(t.to, start + t.length);
    osc.connect(into ?? envelope(ctx, start, t.peak, t.length));
    osc.start(start);
    osc.stop(start + t.length);
  }
  function fizzBuffer(ctx, length, density) {
    const buffer = ctx.createBuffer(1, Math.ceil(ctx.sampleRate * length), ctx.sampleRate);
    const data = buffer.getChannelData(0);
    for (let i = 0; i < data.length; i++) data[i] = (Math.random() * 2 - 1) * (Math.random() < density ? 1 : 0.12);
    return buffer;
  }
  function filteredNoise(ctx, at, buffer, filter, peak, length, attack) {
    const src = ctx.createBufferSource();
    src.buffer = buffer;
    const f = ctx.createBiquadFilter();
    f.type = filter.type;
    f.Q.setValueAtTime(filter.q ?? 0.7, at);
    f.frequency.setValueAtTime(filter.from, at);
    if (filter.to) f.frequency.exponentialRampToValueAtTime(filter.to, at + length);
    src.connect(f);
    f.connect(envelope(ctx, at, peak, length, attack));
    src.start(at);
    src.stop(at + length);
  }
  var RECIPES = {
    /** A soft "tok": a sine dropping an octave in 45 ms. */
    tap: (ctx, at) => tone(ctx, at, { type: "sine", from: 880, to: 440, peak: 0.18, length: 0.045 }),
    /** A dry wood-block tick for the last seconds of the clock. */
    tick: (ctx, at) => tone(ctx, at, { type: "triangle", from: 1480, peak: 0.12, length: 0.05 }),
    /** A rising C-major arpeggio (C6 E6 G6) of bell-like sines with a quiet octave partial; 0.56 s. */
    correct: (ctx, at) => {
      [1046.5, 1318.5, 1568].forEach((f, i) => {
        tone(ctx, at, { type: "sine", from: f, peak: 0.15, length: 0.42, delay: i * 0.07 });
        tone(ctx, at, { type: "sine", from: f * 2, peak: 0.04, length: 0.2, delay: i * 0.07 });
      });
    },
    /** A low, filtered buzz sagging in pitch: two slightly detuned saws beating against each other. */
    wrong: (ctx, at) => {
      const length = 0.26;
      const env = envelope(ctx, at, 0.1, length, 0.01);
      const lowpass = ctx.createBiquadFilter();
      lowpass.type = "lowpass";
      lowpass.frequency.setValueAtTime(900, at);
      lowpass.connect(env);
      for (const f of [146, 155]) tone(ctx, at, { type: "sawtooth", from: f, to: f * 0.75, peak: 0, length }, lowpass);
    },
    /** A tyre-squeal whoosh: noise through a band-pass sweeping down from 2.6 kHz. */
    spin: (ctx, at) => {
      const length = 0.28;
      const buffer = ctx.createBuffer(1, Math.ceil(ctx.sampleRate * length), ctx.sampleRate);
      const data = buffer.getChannelData(0);
      for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
      const noise = ctx.createBufferSource();
      noise.buffer = buffer;
      const band = ctx.createBiquadFilter();
      band.type = "bandpass";
      band.Q.setValueAtTime(3, at);
      band.frequency.setValueAtTime(2600, at);
      band.frequency.exponentialRampToValueAtTime(380, at + length);
      noise.connect(band);
      band.connect(envelope(ctx, at, MAX_GAIN, length, 0.01));
      noise.start(at);
      noise.stop(at + length);
    },
    /** Charging: a short fizz that gets brighter and louder as the pressure (`level` 0..1) rises; 0.22 s. */
    fizz: (ctx, at, level) => {
      const l = Math.min(1, Math.max(0, level));
      filteredNoise(ctx, at, fizzBuffer(ctx, 0.22, 0.04 + 0.08 * l), { type: "highpass", from: 2400 + 3600 * l, q: 0.9 }, 0.03 + 0.08 * l, 0.22, 0.03);
    },
    /**
     * The cork: a hollow "plock" (a sine falling from 640 Hz, the bottle mouth), a low thump and a click of air, then
     * the champagne fizzing out, swelling and dying away; 0.9 s.
     */
    pop: (ctx, at) => {
      tone(ctx, at, { type: "sine", from: 640, to: 250, peak: MAX_GAIN, length: 0.07 });
      tone(ctx, at, { type: "sine", from: 160, to: 48, peak: 0.2, length: 0.14 });
      filteredNoise(ctx, at, fizzBuffer(ctx, 0.03, 1), { type: "bandpass", from: 2200, q: 0.8 }, 0.18, 0.03, 1e-3);
      filteredNoise(ctx, at + 0.03, fizzBuffer(ctx, 0.85, 0.1), { type: "highpass", from: 5200, to: 2600, q: 0.8 }, 0.14, 0.85, 0.06);
    }
  };
  var Sounds = class {
    ctx = null;
    /** A resume() started inside a gesture has not settled yet; sounds scheduled now play once it does. */
    resuming = false;
    /** True once a gesture has created the AudioContext. */
    get unlocked() {
      return this.ctx !== null;
    }
    /** Call from inside a user gesture: creates the AudioContext the first time, and resumes it whenever it is not running. */
    unlock() {
      if (!this.ctx) {
        try {
          const Context = contextClass();
          if (!Context) return;
          this.ctx = new Context();
        } catch {
          return;
        }
        this.blip(this.ctx);
      }
      const ctx = this.ctx;
      if (ctx.state === "running") return;
      try {
        this.resuming = true;
        Promise.resolve(ctx.resume()).catch(() => {
        }).finally(() => this.resuming = false);
      } catch {
        this.resuming = false;
      }
    }
    /** `level` (0..1) shapes sounds that vary, e.g. the fizz rising with the charge; the others ignore it. */
    play(name, level = 1) {
      const ctx = this.ctx;
      if (!ctx || ctx.state !== "running" && !this.resuming) return;
      try {
        if (name.startsWith("crew.") && crewSound(name.slice(5))) {
          playCrewSignature(ctx, name.slice(5), ctx.currentTime + 5e-3);
        } else {
          RECIPES[name](ctx, ctx.currentTime + 5e-3, level);
        }
      } catch {
      }
    }
    /** Older iOS only unlocks output once a sound has started inside the gesture: play one silent sample. */
    blip(ctx) {
      try {
        const src = ctx.createBufferSource();
        src.buffer = ctx.createBuffer(1, 1, ctx.sampleRate);
        src.connect(ctx.destination);
        src.start(0);
      } catch {
      }
    }
  };

  // web/play/src/games/meridian/crew-selection.ts
  var record = (v) => typeof v === "object" && v !== null;
  var CrewSelection = class {
    viewId = null;
    boarding = false;
    armed = false;
    reset() {
      this.viewId = null;
      this.boarding = false;
      this.armed = false;
    }
    observe(view, game) {
      if (game !== "meridian" || view.kind !== "custom" || view.module !== "meridian") {
        this.reset();
        return;
      }
      if (view.viewId === this.viewId) return;
      this.viewId = view.viewId;
      if (!record(view.data)) return;
      if (view.screen === "crewPick") {
        if (!Array.isArray(view.data.crew) || !view.data.crew.some((c) => record(c) && crewSound(c.id))) return;
        const mine = view.data.crew.filter((c) => record(c) && c.mine === true);
        if (!this.boarding) {
          this.boarding = true;
          this.armed = mine.length === 0;
          return;
        }
        if (this.armed && mine.length === 1) {
          this.armed = false;
          return crewSound(mine[0].id);
        }
        return;
      }
      this.boarding = false;
      if (!["watch", "sheet", "turn"].includes(view.screen)) {
        this.reset();
        return;
      }
      if (this.armed && (view.screen === "sheet" || view.screen === "turn") && record(view.data.me)) {
        this.armed = false;
        return crewSound(view.data.me.id);
      }
    }
  };
  return __toCommonJS(stdin_exports);
})();
