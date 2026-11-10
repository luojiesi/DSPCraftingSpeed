* 1.1.1
  * Fix: the speed slider's handle now reaches both ends (100% at the right end, 0.1% at the left end).
* 1.1.0
  * Speed limits are saved in blueprints (through BlueprintTweaks' custom blueprint data) and applied when pasting,
    also to machines that are still under construction.
  * Copy / paste building settings carries the speed limit.
  * New speed limit row in the assembler window: slider, -/+ buttons, mouse wheel (Shift 10%, Ctrl 0.1%), 100% reset,
    and a target-per-minute box. Limits now have 0.1% precision.
  * The window's "per minute" readout shows the limited rate, and the status line shows the limit.
  * Fix: a newly built assembler could inherit the speed limit of a dismantled one.
  * Fix: the proliferator "extra products" progress was not slowed down.
  * Fix: one bad entry in the save data no longer drops all limits after it.
  * Speed limiting no longer allocates memory every tick for every assembler.
  * Preloader source is now part of this repository.
* 1.0.4 Fix slider position
* 1.0.3 Fix preloader patch missing error
* 1.0.0 Mod publish
