# The world's animals: a prompt for the art AI

The animal strips shipped about 3× too big. The TV shrinks them at load (`scale` in `content/tactics/base/props/manifest.json`: mule 0.38,
goat 0.33, fox 0.30, duck 0.25, crow 0.20), which works but loses detail. Redraw at game scale, then remove the `scale` fields.

Measured against a party sprite: about 29 px from feet to head, shoulder about 18 px, hip about 9 px.

> Using the reference image only for its art style, draw a 4-frame walk cycle, frames side by side, of a single [pack mule with grain
> panniers | brown-and-white farm goat | red fox | swimming mallard drake | crow (hop, peck, flap)], side view facing right, at game scale next
> to a 32×40 chibi character whose feet are at y=37, shoulder at y=19 and hip at y=28. Size of the animal:
> - the mule's back 18 px above its feet, ears about 26 px (frame about 32×30);
> - the goat's back 10 px, horns about 15 px (frame about 20×18);
> - the fox's back 7 px, ears about 10 px (frame about 20×12);
> - the duck about 7 px tall (frame about 14×10, waterline on the bottom row);
> - the crow about 6 px (frame about 10×8).
>
> Feet on the bottom row of every frame. Crisp hand-placed pixel art, dark 1-pixel outline, light from the top-left, no anti-aliasing, a
> flat solid #FF00FF background with no magenta fringe left on the animal, no ground, no shadow, no text.

Also:
- One goat frame has a stray pixel to the left of the animal.
- There's no sheep or dog in the pack yet. For the same brief: a sheep's back is about 10 px (frame about 20×16); a farm dog's back is about
  9 px, ears about 12 px (frame about 20×14).
