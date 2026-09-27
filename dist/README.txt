GR Cheat Console Fix v1.3.0
===========================

A fix for "Global Rescue Cheat Console" v0.4.7 (Cheat Console Rebuild 2.5.5 +
Employee Bridge 0.1.7). It gets the console working again on current Global Rescue
builds (tested on v0.5.13).

WHY THE CONSOLE BROKE
Global Rescue's code is obfuscated, and every game update scrambles its internal names
again. The console looked up game data by those scrambled names, so after the update it
couldn't find money, research, employees, bases and so on. This fix finds the same data
by its structure instead of its name, so it should keep working after future updates.

REQUIREMENTS
- BepInEx 5.4.x (win x64)
- Global Rescue Cheat Console v0.4.7. Install BOTH plugin DLLs:
    BepInEx/plugins/GlobalRescueCheatConsole_Rebuild.dll
    BepInEx/plugins/GlobalRescueEmployeeOnlyPatch.dll
  Do NOT install the ProjectAssembly.dll that comes with the console. It was built for an
  older game version and would replace newer game code.

INSTALL
Extract this zip into your Global Rescue folder (the one with GR.exe). You should get:
    BepInEx/plugins/GRCheatConsoleFix.dll

FIXED
- Resources: Set Money / Research / Reputation (and the +/Max buttons)
- Employees: Make Free, Max Stats, Remove Fatigue, Free + Max + No Fatigue, Freeze Stats
- Free wages now stay at $0: the wage slider and loading a save no longer reset them
- Enable All Base Departments
- No Call Cooldowns (now clears the real mission/location cooldowns)
- Freeze Clock / Set Time
- Freeze Employee Stats and Freeze Clock no longer slow the game down
- Staff no longer resign while "maxed": the original Max Stats made every employee permanently
  unsatisfied. Clicking Max Stats or turning on Freeze once repairs affected saves.

ALREADY WORKED, UNCHANGED
Vehicle cheats, research cheats, mission/task completion, restore vehicles.

NOT SUPPORTED
- Unlock DLC Dictionary Flags (unlocks paid DLC; buy the DLC to support the devs)
- Debug/probe buttons

NOTES
- Max Stats sets XP to max, and the game pays experienced staff up to 50% more (unless they're
  on free wages).
- Employees working for $0 are unhappy about their pay. That's how the game works.
  Use "Freeze Employee Stats" to keep their satisfaction at maximum.
- Back up your save before using cheats.
- If something doesn't work, check BepInEx/LogOutput.log for "GR Cheat Console Fix" lines.

CREDITS
Original Cheat Console and Employee Bridge by their respective authors (UnknownGamer / Ksm97).
This fix only patches their plugins at runtime and does not include any of their files.
