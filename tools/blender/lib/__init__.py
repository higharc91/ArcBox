"""Shared helpers for the REDLINE Blender model scripts (see docs/blender-models.md).

Modules (each owned by Task 1; later tasks add *new* modules here rather than editing these):
  frame      Godot <-> Blender coordinate conversion, node orientation for empties
  colour     sRGB hex -> linear RGBA (glTF base colours are linear)
  mesh       pure-Python mesh building in the Godot frame (lofts, sections, airfoils, plates, tubes, lathes, boxes)
  blend      bpy: scene reset, Mesh -> object, modifiers (subsurf, bevel, boolean), apply, join, sharp edges, empties
  materials  named material slots (paint gloss/matte with clearcoat, carbon, rubber, metal, emissive, glass)
  export     glTF binary export with the project's settings
  glb        pure-Python .glb reader (names, triangle counts, bounds, base colours); runs without Blender
  validate   Contract + check(): budgets, required node/material names, dimensions; runs without Blender
  asset      Context handed to every asset module's build()
  preview    Cycles preview renders (no denoiser in this Blender build)
"""
