// Inkitty sample: a small multi-file story.
INCLUDE chapters/forest.ink
INCLUDE chapters/town.ink

VAR has_torch = false
CONST MAX_COURAGE = 3

-> start

=== start ===
Once upon a time, at the edge of a dark wood, you stood at a crossroads.
TODO: write a better opening line
* [Go left, into the forest] -> forest
* [Go right, towards the town] -> town.square
* {has_torch} [Light the torch first] -> light_torch

=== light_torch ===
~ has_torch = true
The torch flickers into life.
-> start
