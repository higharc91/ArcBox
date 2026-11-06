/** Explicit interruption targets shared by the browser driver and its small regression suite. */
const phases = {
  round2: { phone: ['throttle', 'throttle-locked', 'reading', 'question', 'answered'], tv: ['Throttle', 'Reading', 'Question'], round: 2 },
  qualifying: { phone: ['quali-question'], tv: ['QualifyingQuestion'], round: 0 },
  question: { phone: ['question'], tv: ['Question'], round: 2 },
  reveal: { phone: ['reveal'], tv: ['Reveal'], round: 2 },
  anthem: { phone: ['anthem'], tv: ['Anthem'], round: 1 },
  champagne: { phone: ['champagne'], tv: ['Champagne'], round: 1 },
};
function policy(name = 'round2') {
  if (!Object.hasOwn(phases, name)) throw new Error(`Unknown restart phase: ${name}`);
  return { name, ...phases[name] };
}
function latestPhase(log) {
  return [...log.matchAll(/^REDLINE ([A-Za-z]+) round \d+\r?$/gm)].at(-1)?.[1];
}
function matches(target, phonePhase, round, tvLog) {
  return round >= target.round && target.phone.includes(phonePhase)
    && (tvLog === undefined || target.tv.includes(latestPhase(tvLog)));
}
module.exports = { policy, latestPhase, matches };
