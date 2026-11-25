=== forest ===
The forest is dark. # mood: gloomy
* (light) [Light a torch]
    ~ has_torch = true
    You can see a path ahead.
    -> forest.clearing
* [Turn back] -> start

= clearing
A quiet clearing. Birds sing.
{ courage() > 1: You feel brave. | You feel uneasy. }
-> town.square

=== function courage() ===
~ return MAX_COURAGE - 1
