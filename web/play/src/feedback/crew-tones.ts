/** Original, wordless selection signatures. No samples, speech or paid generation. */
export interface CrewTone { frequency: number; to?: number; delay: number; length: number }
export const CREW_SIGNATURES = {
  ines_okafor: { name: "Ines Okafor", character: "a warm, reassuring toolbox chime", wave: "triangle", notes: [392, 494, 587] },
  tobe_raske: { name: "Tobe Raske", character: "a round little two-part rigging plonk", wave: "sine", notes: [262, 392, 330] },
  sable_quist: { name: "Sable Quist", character: "a light upward pilot whistle", wave: "sine", notes: [523, 659, 880] },
  anselm_varga: { name: "Dr. Anselm Varga", character: "a gentle, rounded medical scanner reply", wave: "sine", notes: [440, 554, 440] },
  pell_ostrander: { name: "Pell Ostrander", character: "a playful three-note terminal greeting", wave: "triangle", notes: [659, 523, 784] },
  juno_achterberg: { name: "Juno Achterberg", character: "a sturdy little cargo-latch boop", wave: "triangle", notes: [294, 440, 587] },
  kit_sato: { name: "Kit Sato", character: "a curious quick scout chirrup", wave: "sine", notes: [740, 988, 831] },
  rhee_callender: { name: "Rhee Callender", character: "a soft, sparkling radio hello", wave: "triangle", notes: [587, 784, 988] },
} as const;
export type CrewId = keyof typeof CREW_SIGNATURES;
export type CrewSound = `crew.${CrewId}`;
export const CREW_GAIN = 0.12;
export const CREW_DURATION = 0.42;
export function crewSound(id: unknown): CrewSound | undefined {
  return typeof id === "string" && Object.hasOwn(CREW_SIGNATURES, id) ? `crew.${id as CrewId}` : undefined;
}

/** Shared by live phone output and OfflineAudioContext review renders. */
export function playCrewSignature(ctx: AudioContext | OfflineAudioContext, id: CrewId, at: number): void {
  const signature = CREW_SIGNATURES[id];
  signature.notes.forEach((frequency, i) => {
    const start = at + i * 0.075;
    const length = i === 2 ? 0.27 : 0.18;
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = signature.wave;
    osc.frequency.setValueAtTime(frequency, start);
    osc.frequency.exponentialRampToValueAtTime(frequency * (i === 2 ? 1.035 : 1), start + length);
    gain.gain.setValueAtTime(0.0001, start);
    gain.gain.exponentialRampToValueAtTime(CREW_GAIN, start + 0.009);
    gain.gain.exponentialRampToValueAtTime(CREW_GAIN * 0.35, start + 0.08);
    gain.gain.exponentialRampToValueAtTime(0.0001, start + length);
    osc.connect(gain);gain.connect(ctx.destination);
    osc.onended = () => { osc.disconnect();gain.disconnect(); };
    osc.start(start);osc.stop(start + length);
  });
}
